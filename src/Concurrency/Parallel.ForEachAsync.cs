// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if !NET6_0_OR_GREATER
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

#nullable enable

namespace System.Threading.Tasks
{
    public static class ParallelShims
    {
        /// <summary>Executes a for each operation on an <see cref="IEnumerable{TSource}"/> in which iterations may run in parallel.</summary>
        /// <typeparam name="TSource">The type of the data in the source.</typeparam>
        /// <param name="source">An enumerable data source.</param>
        /// <param name="body">An asynchronous delegate that is invoked once per element in the data source.</param>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the <paramref name="source"/> argument or <paramref name="body"/> argument is null.</exception>
        /// <returns>A task that represents the entire for each operation.</returns>
        /// <remarks>The operation will execute at most <see cref="Environment.ProcessorCount"/> operations in parallel.</remarks>
        public static Task ForEachAsync<TSource>(IEnumerable<TSource> source, Func<TSource, CancellationToken, ValueTask> body)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (body is null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            return ForEachAsync(source, DefaultDegreeOfParallelism, TaskScheduler.Default, default, body);
        }

        /// <summary>Executes a for each operation on an <see cref="IEnumerable{TSource}"/> in which iterations may run in parallel.</summary>
        /// <typeparam name="TSource">The type of the data in the source.</typeparam>
        /// <param name="source">An enumerable data source.</param>
        /// <param name="cancellationToken">A cancellation token that may be used to cancel the for each operation.</param>
        /// <param name="body">An asynchronous delegate that is invoked once per element in the data source.</param>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the <paramref name="source"/> argument or <paramref name="body"/> argument is null.</exception>
        /// <returns>A task that represents the entire for each operation.</returns>
        /// <remarks>The operation will execute at most <see cref="Environment.ProcessorCount"/> operations in parallel.</remarks>
        public static Task ForEachAsync<TSource>(IEnumerable<TSource> source, CancellationToken cancellationToken, Func<TSource, CancellationToken, ValueTask> body)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (body is null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            return ForEachAsync(source, DefaultDegreeOfParallelism, TaskScheduler.Default, cancellationToken, body);
        }

        /// <summary>Executes a for each operation on an <see cref="IEnumerable{TSource}"/> in which iterations may run in parallel.</summary>
        /// <typeparam name="TSource">The type of the data in the source.</typeparam>
        /// <param name="source">An enumerable data source.</param>
        /// <param name="parallelOptions">An object that configures the behavior of this operation.</param>
        /// <param name="body">An asynchronous delegate that is invoked once per element in the data source.</param>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the <paramref name="source"/> argument or <paramref name="body"/> argument is null.</exception>
        /// <returns>A task that represents the entire for each operation.</returns>
        public static Task ForEachAsync<TSource>(IEnumerable<TSource> source, ParallelOptions parallelOptions, Func<TSource, CancellationToken, ValueTask> body)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (parallelOptions is null)
            {
                throw new ArgumentNullException(nameof(parallelOptions));
            }
            if (body is null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            return ForEachAsync(source, parallelOptions.EffectiveMaxConcurrencyLevel(), parallelOptions.TaskScheduler ?? TaskScheduler.Current, parallelOptions.CancellationToken, body);
        }

        /// <summary>Executes a for each operation on an <see cref="IEnumerable{TSource}"/> in which iterations may run in parallel.</summary>
        /// <typeparam name="TSource">The type of the data in the source.</typeparam>
        /// <param name="source">An enumerable data source.</param>
        /// <param name="dop">A integer indicating how many operations to allow to run in parallel.</param>
        /// <param name="scheduler">The task scheduler on which all code should execute.</param>
        /// <param name="cancellationToken">A cancellation token that may be used to cancel the for each operation.</param>
        /// <param name="body">An asynchronous delegate that is invoked once per element in the data source.</param>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the <paramref name="source"/> argument or <paramref name="body"/> argument is null.</exception>
        /// <returns>A task that represents the entire for each operation.</returns>
        private static async Task ForEachAsync<TSource>(IEnumerable<TSource> source, int dop, TaskScheduler scheduler, CancellationToken cancellationToken, Func<TSource, CancellationToken, ValueTask> body)
        {
            Debug.Assert(source != null);
            Debug.Assert(scheduler != null);
            Debug.Assert(body != null);

            // One fast up-front check for cancellation before we start the whole operation.
            if (cancellationToken.IsCancellationRequested)
            {
                await Task.FromCanceled(cancellationToken);
                return;
            }

            if (dop < 0)
            {
                dop = DefaultDegreeOfParallelism;
            }

            // The worker body. Each worker will execute this same body.
            static async Task taskBody(object o)
            {
                SyncForEachAsyncState<TSource> state = (SyncForEachAsyncState<TSource>)o;
                bool launchedNext = false;
                try
                {
                    // Continue to loop while there are more elements to be processed.
                    while (!state.Cancellation.IsCancellationRequested)
                    {
                        // Get the next element from the enumerator.  This requires asynchronously locking around MoveNextAsync/Current.
                        TSource element;
                        lock (state)
                        {
                            if (!state.Enumerator.MoveNext())
                            {
                                break;
                            }

                            element = state.Enumerator.Current;
                        }

                        // If the remaining dop allows it and we've not yet queued the next worker, do so now.  We wait
                        // until after we've grabbed an item from the enumerator to a) avoid unnecessary contention on the
                        // serialized resource, and b) avoid queueing another work if there aren't any more items.  Each worker
                        // is responsible only for creating the next worker, which in turn means there can't be any contention
                        // on creating workers (though it's possible one worker could be executing while we're creating the next).
                        if (!launchedNext)
                        {
                            launchedNext = true;
                            state.QueueWorkerIfDopAvailable();
                        }

                        // Process the loop body.
                        await state.LoopBody(element, state.Cancellation.Token);
                    }
                }
                catch (Exception e)
                {
                    // Record the failure and then don't let the exception propagate.  The last worker to complete
                    // will propagate exceptions as is appropriate to the top-level task.
                    state.RecordException(e);
                }
                finally
                {
                    // If we're the last worker to complete, clean up and complete the operation.
                    if (state.SignalWorkerCompletedIterating())
                    {
                        try
                        {
                            state.Dispose();
                        }
                        catch (Exception e)
                        {
                            state.RecordException(e);
                        }

                        // Finally, complete the task returned to the ForEachAsync caller.
                        // This must be the very last thing done.
                        state.Complete();
                    }
                }
            }

            try
            {
                // Construct a state object that encapsulates all state to be passed and shared between
                // the workers, and queues the first worker.
                SyncForEachAsyncState<TSource> state = new(source, taskBody, dop, scheduler, cancellationToken, body);
                state.QueueWorkerIfDopAvailable();
                await state.Task;
                return;
            }
            catch (Exception e)
            {
                await Task.FromException(e);
                return;
            }
        }

        /// <summary>Executes a for each operation on an <see cref="IEnumerable{TSource}"/> in which iterations may run in parallel.</summary>
        /// <typeparam name="TSource">The type of the data in the source.</typeparam>
        /// <param name="source">An enumerable data source.</param>
        /// <param name="body">An asynchronous delegate that is invoked once per element in the data source.</param>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the <paramref name="source"/> argument or <paramref name="body"/> argument is null.</exception>
        /// <returns>A task that represents the entire for each operation.</returns>
        /// <remarks>The operation will execute at most <see cref="Environment.ProcessorCount"/> operations in parallel.</remarks>
        public static Task ForEachAsync<TSource>(IAsyncEnumerable<TSource> source, Func<TSource, CancellationToken, ValueTask> body)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (body is null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            return ForEachAsync(source, DefaultDegreeOfParallelism, TaskScheduler.Default, default, body);
        }

        /// <summary>Executes a for each operation on an <see cref="IEnumerable{TSource}"/> in which iterations may run in parallel.</summary>
        /// <typeparam name="TSource">The type of the data in the source.</typeparam>
        /// <param name="source">An enumerable data source.</param>
        /// <param name="cancellationToken">A cancellation token that may be used to cancel the for each operation.</param>
        /// <param name="body">An asynchronous delegate that is invoked once per element in the data source.</param>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the <paramref name="source"/> argument or <paramref name="body"/> argument is null.</exception>
        /// <returns>A task that represents the entire for each operation.</returns>
        /// <remarks>The operation will execute at most <see cref="Environment.ProcessorCount"/> operations in parallel.</remarks>
        public static Task ForEachAsync<TSource>(IAsyncEnumerable<TSource> source, CancellationToken cancellationToken, Func<TSource, CancellationToken, ValueTask> body)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (body is null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            return ForEachAsync(source, DefaultDegreeOfParallelism, TaskScheduler.Default, cancellationToken, body);
        }

        public static int EffectiveMaxConcurrencyLevel(this ParallelOptions parallelOptions)
        {
            int rval = parallelOptions.MaxDegreeOfParallelism;
            int schedulerMax = (parallelOptions.TaskScheduler ?? TaskScheduler.Current).MaximumConcurrencyLevel;
            if ((schedulerMax > 0) && (schedulerMax != int.MaxValue))
            {
                rval = (rval == -1) ? schedulerMax : Math.Min(schedulerMax, rval);
            }
            return rval;
        }

        /// <summary>Executes a for each operation on an <see cref="IEnumerable{TSource}"/> in which iterations may run in parallel.</summary>
        /// <typeparam name="TSource">The type of the data in the source.</typeparam>
        /// <param name="source">An enumerable data source.</param>
        /// <param name="parallelOptions">An object that configures the behavior of this operation.</param>
        /// <param name="body">An asynchronous delegate that is invoked once per element in the data source.</param>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the <paramref name="source"/> argument or <paramref name="body"/> argument is null.</exception>
        /// <returns>A task that represents the entire for each operation.</returns>
        public static Task ForEachAsync<TSource>(IAsyncEnumerable<TSource> source, ParallelOptions parallelOptions, Func<TSource, CancellationToken, ValueTask> body)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (parallelOptions is null)
            {
                throw new ArgumentNullException(nameof(parallelOptions));
            }
            if (body is null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            return ForEachAsync(source, parallelOptions.EffectiveMaxConcurrencyLevel(), parallelOptions.TaskScheduler ?? TaskScheduler.Current, parallelOptions.CancellationToken, body);
        }

        /// <summary>Executes a for each operation on an <see cref="IEnumerable{TSource}"/> in which iterations may run in parallel.</summary>
        /// <typeparam name="TSource">The type of the data in the source.</typeparam>
        /// <param name="source">An enumerable data source.</param>
        /// <param name="dop">A integer indicating how many operations to allow to run in parallel.</param>
        /// <param name="scheduler">The task scheduler on which all code should execute.</param>
        /// <param name="cancellationToken">A cancellation token that may be used to cancel the for each operation.</param>
        /// <param name="body">An asynchronous delegate that is invoked once per element in the data source.</param>
        /// <exception cref="ArgumentNullException">The exception that is thrown when the <paramref name="source"/> argument or <paramref name="body"/> argument is null.</exception>
        /// <returns>A task that represents the entire for each operation.</returns>
        private static async Task ForEachAsync<TSource>(IAsyncEnumerable<TSource> source, int dop, TaskScheduler scheduler, CancellationToken cancellationToken, Func<TSource, CancellationToken, ValueTask> body)
        {
            Debug.Assert(source != null);
            Debug.Assert(scheduler != null);
            Debug.Assert(body != null);

            // One fast up-front check for cancellation before we start the whole operation.
            if (cancellationToken.IsCancellationRequested)
            {
                await Task.FromCanceled(cancellationToken);
                return;
            }

            if (dop < 0)
            {
                dop = DefaultDegreeOfParallelism;
            }

            // The worker body. Each worker will execute this same body.
            static async Task taskBody(object o)
            {
                AsyncForEachAsyncState<TSource> state = (AsyncForEachAsyncState<TSource>)o;
                bool launchedNext = false;
                try
                {
                    // Continue to loop while there are more elements to be processed.
                    while (!state.Cancellation.IsCancellationRequested)
                    {
                        // Get the next element from the enumerator.  This requires asynchronously locking around MoveNextAsync/Current.
                        TSource element;
                        await state.Lock.WaitAsync(state.Cancellation.Token);
                        try
                        {
                            if (!await state.Enumerator.MoveNextAsync())
                            {
                                break;
                            }

                            element = state.Enumerator.Current;
                        }
                        finally
                        {
                            state.Lock.Release();
                        }

                        // If the remaining dop allows it and we've not yet queued the next worker, do so now.  We wait
                        // until after we've grabbed an item from the enumerator to a) avoid unnecessary contention on the
                        // serialized resource, and b) avoid queueing another work if there aren't any more items.  Each worker
                        // is responsible only for creating the next worker, which in turn means there can't be any contention
                        // on creating workers (though it's possible one worker could be executing while we're creating the next).
                        if (!launchedNext)
                        {
                            launchedNext = true;
                            state.QueueWorkerIfDopAvailable();
                        }

                        // Process the loop body.
                        await state.LoopBody(element, state.Cancellation.Token);
                    }
                }
                catch (Exception e)
                {
                    // Record the failure and then don't let the exception propagate.  The last worker to complete
                    // will propagate exceptions as is appropriate to the top-level task.
                    state.RecordException(e);
                }
                finally
                {
                    // If we're the last worker to complete, clean up and complete the operation.
                    if (state.SignalWorkerCompletedIterating())
                    {
                        try
                        {
                            await state.DisposeAsync();
                        }
                        catch (Exception e)
                        {
                            state.RecordException(e);
                        }

                        // Finally, complete the task returned to the ForEachAsync caller.
                        // This must be the very last thing done.
                        state.Complete();
                    }
                }
            }

            try
            {
                // Construct a state object that encapsulates all state to be passed and shared between
                // the workers, and queues the first worker.
                AsyncForEachAsyncState<TSource> state = new(source, taskBody, dop, scheduler, cancellationToken, body);
                state.QueueWorkerIfDopAvailable();
                await state.Task;
                return;
            }
            catch (Exception e)
            {
                await Task.FromException(e);
                return;
            }
        }

        /// <summary>Gets the default degree of parallelism to use when none is explicitly provided.</summary>
        private static int DefaultDegreeOfParallelism => Environment.ProcessorCount;

        /// <summary>Stores the state associated with a ForEachAsync operation, shared between all its workers.</summary>
        /// <typeparam name="TSource">Specifies the type of data being enumerated.</typeparam>
        private abstract class ForEachAsyncState<TSource> : TaskCompletionSource<object>
        {
            /// <summary>The caller-provided cancellation token.</summary>
            private readonly CancellationToken _externalCancellationToken;
            /// <summary>Registration with caller-provided cancellation token.</summary>
            protected readonly CancellationTokenRegistration _registration;
            /// <summary>
            /// The delegate to invoke on each worker to run the enumerator processing loop.
            /// </summary>
            /// <remarks>
            /// This could have been an action rather than a func, but it returns a task so that the task body is an async Task
            /// method rather than async void, even though the worker body catches all exceptions and the returned Task is ignored.
            /// </remarks>
            private readonly Func<object, Task> _taskBody;
            /// <summary>The <see cref="TaskScheduler"/> on which all work should be performed.</summary>
            private readonly TaskScheduler _scheduler;
            /// <summary>The <see cref="ExecutionContext"/> present at the time of the ForEachAsync invocation.  This is only used if on the default scheduler.</summary>
            private readonly ExecutionContext? _executionContext;

            /// <summary>The number of outstanding workers.  When this hits 0, the operation has completed.</summary>
            private int _completionRefCount;
            /// <summary>Any exceptions incurred during execution.</summary>
            private List<Exception>? _exceptions;
            /// <summary>The number of workers that may still be created.</summary>
            private int _remainingDop;

            /// <summary>The delegate to invoke for each element yielded by the enumerator.</summary>
            public readonly Func<TSource, CancellationToken, ValueTask> LoopBody;
            /// <summary>The internal token source used to cancel pending work.</summary>
            public readonly CancellationTokenSource Cancellation = new();

            /// <summary>Initializes the state object.</summary>
            protected ForEachAsyncState(Func<object, Task> taskBody, int dop, TaskScheduler scheduler, CancellationToken cancellationToken, Func<TSource, CancellationToken, ValueTask> body)
            {
                _taskBody = taskBody;
                _remainingDop = dop;
                LoopBody = body;
                _scheduler = scheduler;
                if (scheduler == TaskScheduler.Default)
                {
                    _executionContext = ExecutionContext.Capture();
                }

                _externalCancellationToken = cancellationToken;

                _registration = cancellationToken.Register(static o => ((ForEachAsyncState<TSource>)o!).Cancellation.Cancel(), this);
            }

            /// <summary>Queues another worker if allowed by the remaining degree of parallelism permitted.</summary>
            /// <remarks>This is not thread-safe and must only be invoked by one worker at a time.</remarks>
            public void QueueWorkerIfDopAvailable()
            {
                if (_remainingDop > 0)
                {
                    _remainingDop--;

                    // Queue the invocation of the worker/task body.  Note that we explicitly do not pass a cancellation token here,
                    // as the task body is what's responsible for completing the ForEachAsync task, for decrementing the reference count
                    // on pending tasks, and for cleaning up state.  If a token were passed to StartNew (which simply serves to stop the
                    // task from starting to execute if it hasn't yet by the time cancellation is requested), all of that logic could be
                    // skipped, and bad things could ensue, e.g. deadlocks, leaks, etc.  Also note that we need to increment the pending
                    // work item ref count prior to queueing the worker in order to avoid race conditions that could lead to temporarily
                    // and erroneously bouncing at zero, which would trigger completion too early.
                    Interlocked.Increment(ref _completionRefCount);
                    if (_scheduler == TaskScheduler.Default)
                    {
                        // If the scheduler is the default, we can avoid the overhead of the StartNew Task by just queueing
                        // this state object as the work item.
                        ThreadPool.UnsafeQueueUserWorkItem(RunWrapper, this);
                    }
                    else
                    {

                        // We're targeting a non-default TaskScheduler, so queue the task body to it.
                        System.Threading.Tasks.Task.Factory.StartNew(_taskBody!, this, default, TaskCreationOptions.DenyChildAttach, _scheduler);
                    }
                }
            }

            /// <summary>Signals that the worker has completed iterating.</summary>
            /// <returns>true if this is the last worker to complete iterating; otherwise, false.</returns>
            public bool SignalWorkerCompletedIterating()
            {
                return Interlocked.Decrement(ref _completionRefCount) == 0;
            }

            /// <summary>Stores an exception and triggers cancellation in order to alert all workers to stop as soon as possible.</summary>
            /// <param name="e">The exception.</param>
            public void RecordException(Exception e)
            {
                lock (this)
                {
                    (_exceptions ??= new List<Exception>()).Add(e);
                }

                Cancellation.Cancel();
            }

            /// <summary>Completes the ForEachAsync task based on the status of this state object.</summary>
            public void Complete()
            {
                Debug.Assert(_completionRefCount == 0, $"Expected {nameof(_completionRefCount)} == 0, got {_completionRefCount}");

                bool taskSet;
                if (_externalCancellationToken.IsCancellationRequested)
                {
                    // The externally provided token had cancellation requested. Assume that any exceptions
                    // then are due to that, and just cancel the resulting task.
                    taskSet = TrySetCanceled(_externalCancellationToken);
                }
                else if (_exceptions is null)
                {
                    // Everything completed successfully.
                    taskSet = TrySetResult(this);
                }
                else
                {
                    // Fault with all of the received exceptions, but filter out those due to inner cancellation,
                    // as they're effectively an implementation detail and stem from the original exception.
                    Debug.Assert(_exceptions.Count > 0, "If _exceptions was created, it should have also been populated.");
                    for (int i = 0; i < _exceptions.Count; i++)
                    {
                        if (_exceptions[i] is OperationCanceledException oce && oce.CancellationToken == Cancellation.Token)
                        {
                            _exceptions[i] = null!;
                        }
                    }
                    _exceptions.RemoveAll(e => e is null);
                    Debug.Assert(_exceptions.Count > 0, "Since external cancellation wasn't requested, there should have been a non-cancellation exception that triggered internal cancellation.");
                    taskSet = TrySetException(_exceptions);
                }

                Debug.Assert(taskSet, "Complete should only be called once.");
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static void RunWrapper(object state)
            {
                var asyncState = (ForEachAsyncState<TSource>)state;
                asyncState.Execute();
            }
            /// <summary>Executes the task body using the <see cref="ExecutionContext"/> captured when ForEachAsync was invoked.</summary>
            void Execute()
            {
                Debug.Assert(_scheduler == TaskScheduler.Default, $"Expected {nameof(_scheduler)} == TaskScheduler.Default, got {_scheduler}");

                if (_executionContext is null)
                {
                    _taskBody(this);
                }
                else
                {
                    ExecutionContext.Run(_executionContext, static o => ((ForEachAsyncState<TSource>)o!)._taskBody(o), this);
                }
            }
        }

        /// <summary>Stores the state associated with an IEnumerable ForEachAsync operation, shared between all its workers.</summary>
        /// <typeparam name="TSource">Specifies the type of data being enumerated.</typeparam>
        private sealed class SyncForEachAsyncState<TSource> : ForEachAsyncState<TSource>, IDisposable
        {
            public readonly IEnumerator<TSource> Enumerator;

            public SyncForEachAsyncState(
                IEnumerable<TSource> source, Func<object, Task> taskBody,
                int dop, TaskScheduler scheduler, CancellationToken cancellationToken,
                Func<TSource, CancellationToken, ValueTask> body) :
                base(taskBody, dop, scheduler, cancellationToken, body)
            {
                Enumerator = source.GetEnumerator() ?? throw new InvalidOperationException();
            }

            public void Dispose()
            {
                _registration.Dispose();
                Enumerator.Dispose();
            }
        }

        /// <summary>Stores the state associated with an IAsyncEnumerable ForEachAsync operation, shared between all its workers.</summary>
        /// <typeparam name="TSource">Specifies the type of data being enumerated.</typeparam>
        private sealed class AsyncForEachAsyncState<TSource> : ForEachAsyncState<TSource>, IAsyncDisposable
        {
            public readonly SemaphoreSlim Lock = new(1, 1);
            public readonly IAsyncEnumerator<TSource> Enumerator;

            public AsyncForEachAsyncState(
                IAsyncEnumerable<TSource> source, Func<object, Task> taskBody,
                int dop, TaskScheduler scheduler, CancellationToken cancellationToken,
                Func<TSource, CancellationToken, ValueTask> body) :
                base(taskBody, dop, scheduler, cancellationToken, body)
            {
                Enumerator = source.GetAsyncEnumerator(Cancellation.Token) ?? throw new InvalidOperationException();
            }

            public ValueTask DisposeAsync()
            {
                _registration.Dispose();
                return Enumerator.DisposeAsync();
            }
        }
    }
}
#endif