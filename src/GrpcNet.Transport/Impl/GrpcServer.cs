namespace GrpcNet.Transport.Impl
{
    using Grpc.Core;
    using Microsoft.Extensions.Logging;
    using Concurrency;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Net.Sockets;
    using System.Threading.Tasks;
    using GrpcNet.Abstractions;

    internal sealed class GrpcServer : ServiceBinderBase, IAsyncDisposable
    {
        private delegate Task CallHandlerAsync(GrpcServerIncomingCall incomingCall);

        private readonly ITransportListener _listener;
        private readonly ILogger? _logger;
        private readonly CancellationTokenSource _cts;
        private readonly Task _loop;
        private readonly ConcurrentDictionary<TrackedCall, bool> _clients;
        private readonly Dictionary<string, CallHandlerAsync> _callHandlers;

        private class TrackedCall
        {
            public required ITransportAdapter Client;
            public Task? Task;
        }

        public GrpcServer(
            ITransportListener listener,
            ILogger? logger = null)
        {
            _listener = listener;
            _listener.Start();
            _listener.NoDelay = true;
            _logger = logger;
            _cts = new CancellationTokenSource();
            _clients = new ConcurrentDictionary<TrackedCall, bool>();
            _callHandlers = new Dictionary<string, CallHandlerAsync>();
            _loop = Task.Run(ProcessAsync);
        }

        private void LogTrace(string message)
        {
            if (_logger != null)
            {
                _logger.LogTrace($"TcpGrpcServer: {message}");
            }
        }

        private async Task ProcessAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                var nextCall = await _listener.AcceptAsync(_cts.Token).ConfigureAwait(false);
                if (nextCall == null)
                {
                    continue;
                }

                LogTrace($"Accepted connection from '{nextCall.RemoteEndPoint}'.");

                var tracked = new TrackedCall
                {
                    Client = nextCall
                };
                _clients.TryAdd(tracked, true);
                tracked.Task = Task.Run(async () => await ProcessCallAsync(tracked).ConfigureAwait(false));
            }
        }

        private async Task ProcessCallAsync(TrackedCall incomingCall)
        {
            try
            {
                await using (new GrpcTransportConnection(incomingCall.Client, _logger, false).AsAsyncDisposable(out var connection))
                {
                    // Cached so we have it for logging even after the client is disposed.
                    var remoteEndpoint = incomingCall.Client.RemoteEndPoint;

                    // Read the call initialization.
                    var request = await connection.ReadExpectedAsync<GrpcRequest>(GrpcRequest.Descriptor, _cts.Token).ConfigureAwait(false);
                    LogTrace($"'{remoteEndpoint}': Received request to call '{request.FullName}'.");

                    // Lookup the call handler.
                    if (!_callHandlers.TryGetValue(request.FullName, out var handler))
                    {
                        // This API method is not supported.
                        LogTrace($"'{remoteEndpoint}': Requested method is not implemented.");
                        await connection.WriteAsync(new GrpcMessage
                        {
                            Type = GrpcMessageType.ResponseComplete,
                        }, CancellationToken.None).ConfigureAwait(false);
                        await connection.WriteAsync(new GrpcResponseComplete
                        {
                            StatusCode = (int)StatusCode.Unimplemented,
                            StatusDetails = "This method is not implemented.",
                            HasResponseTrailers = false,
                        }, CancellationToken.None).ConfigureAwait(false);
                        return;
                    }

                    // Invoke the call handler to handle the rest.
                    LogTrace($"'{remoteEndpoint}': Invoking call handler.");
                    await handler(new GrpcServerIncomingCall
                    {
                        Request = request,
                        Connection = connection,
                        Peer = remoteEndpoint!.AddressFamily switch
                        {
                            AddressFamily.InterNetwork => $"ipv4:{remoteEndpoint}",
                            AddressFamily.InterNetworkV6 => $"ipv6:{remoteEndpoint}",
                            _ => "unknown",
                        },
                        LogTrace = message =>
                        {
                            LogTrace($"'{remoteEndpoint}': {message}");
                        }
                    }).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                throw;
            }
            finally
            {
                _clients.TryRemove(incomingCall, out _);
            }
        }

        private async Task ProcessUnaryCallAsync<TRequest, TResponse>(
            GrpcServerIncomingCall incoming,
            Method<TRequest, TResponse> method,
            UnaryServerMethod<TRequest, TResponse> handler)
            where TRequest : class
            where TResponse : class
        {
            // Set up the call context.
            incoming.LogTrace($"Creating server call context.");
            var writeMutex = new Mutex();
            using var serverCallContext = incoming.CreateCallContext(
                method.Name,
                _cts.Token);
            var serverCall = new GrpcServerCall<TRequest, TResponse>(
                incoming,
                serverCallContext,
                method,
                GrpcCallType.Unary);

            // Read the request data since this is not a streaming request.
            var requestData = await serverCall.TryReadNonStreamingClientRequestDataAsync()
                .ConfigureAwait(false);
            if (requestData == null)
            {
                // Client misbehaved or cancelled. TryReadNonStreamingClientRequestDataAsync
                // handles sending the required responses.
                return;
            }

            // Invoke the handler, and monitor for client side cancellation. This also
            // sends the required status responses.
            await serverCall.InvokeHandlerWithClientMonitoringAsync(async () =>
            {
                return await handler(requestData, serverCallContext).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        private async Task ProcessClientStreamingCallAsync<TRequest, TResponse>(
            GrpcServerIncomingCall incoming,
            Method<TRequest, TResponse> method,
            ClientStreamingServerMethod<TRequest, TResponse> handler)
            where TRequest : class
            where TResponse : class
        {
            // Set up the call context.
            incoming.LogTrace($"Creating server call context.");
            var writeMutex = new Mutex();
            using var serverCallContext = incoming.CreateCallContext(
                method.Name,
                _cts.Token);
            var serverCall = new GrpcServerCall<TRequest, TResponse>(
                incoming,
                serverCallContext,
                method,
                GrpcCallType.ClientStreaming);

            // Invoke the handler, and monitor for client side cancellation. This also
            // sends the required status responses.
            await serverCall.InvokeHandlerWithClientMonitoringAsync(async () =>
            {
                return await handler(serverCall, serverCallContext).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        private async Task ProcessServerStreamingCallAsync<TRequest, TResponse>(
            GrpcServerIncomingCall incoming,
            Method<TRequest, TResponse> method,
            ServerStreamingServerMethod<TRequest, TResponse> handler)
            where TRequest : class
            where TResponse : class
        {
            // Set up the call context.
            incoming.LogTrace($"Creating server call context.");
            var writeMutex = new Mutex();
            using var serverCallContext = incoming.CreateCallContext(
                method.Name,
                _cts.Token);
            var serverCall = new GrpcServerCall<TRequest, TResponse>(
                incoming,
                serverCallContext,
                method,
                GrpcCallType.ServerStreaming);

            // Read the request data since this is not a streaming request.
            var requestData = await serverCall.TryReadNonStreamingClientRequestDataAsync()
                .ConfigureAwait(false);
            if (requestData == null)
            {
                // Client misbehaved or cancelled. TryReadNonStreamingClientRequestDataAsync
                // handles sending the required responses.
                return;
            }

            // Invoke the handler, and monitor for client side cancellation. This also
            // sends the required status responses.
            await serverCall.InvokeHandlerWithClientMonitoringAsync(async () =>
            {
                await handler(requestData, serverCall, serverCallContext).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        }

        private async Task ProcessDuplexStreamingCallAsync<TRequest, TResponse>(
            GrpcServerIncomingCall incoming,
            Method<TRequest, TResponse> method,
            DuplexStreamingServerMethod<TRequest, TResponse> handler)
            where TRequest : class
            where TResponse : class
        {
            // Set up the call context.
            incoming.LogTrace($"Creating server call context.");
            var writeMutex = new Mutex();
            using var serverCallContext = incoming.CreateCallContext(
                method.Name,
                _cts.Token);
            var serverCall = new GrpcServerCall<TRequest, TResponse>(
                incoming,
                serverCallContext,
                method,
                GrpcCallType.DuplexStreaming);

            // Invoke the handler, and monitor for client side cancellation. This also
            // sends the required status responses.
            await serverCall.InvokeHandlerWithClientMonitoringAsync(async () =>
            {
                await handler(serverCall, serverCall, serverCallContext).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try
            {
                LogTrace("Waiting for accept loop to stop.");
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            try
            {
                foreach (var client in _clients.Keys.ToArray())
                {
                    try
                    {
                        LogTrace("Waiting for client handler stop.");
                        await client.Task!.ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception)
            {
            }
            _listener.Stop();
            _listener.Dispose();
            _cts.Dispose();
            LogTrace("TCP gRPC server has been shutdown.");
        }

        #region ServiceBinderBase

        public override void AddMethod<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            UnaryServerMethod<TRequest, TResponse> handler)
        {
#if !NETSTANDARD
            ArgumentNullException.ThrowIfNull(method);
#else
            if (method is null)
                throw new ArgumentNullException(nameof(method));
#endif
            _callHandlers[method.FullName] = async (incomingCall) =>
            {
                await ProcessUnaryCallAsync(
                    incomingCall,
                    method,
                    handler).ConfigureAwait(false);
            };
        }

        public override void AddMethod<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            ClientStreamingServerMethod<TRequest, TResponse> handler)
        {
#if !NETSTANDARD
            ArgumentNullException.ThrowIfNull(method);
#else
            if (method is null) 
                throw new ArgumentNullException(nameof(method));
#endif
            _callHandlers[method.FullName] = async (incomingCall) =>
            {
                await ProcessClientStreamingCallAsync(
                    incomingCall,
                    method,
                    handler).ConfigureAwait(false);
            };
        }

        public override void AddMethod<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            ServerStreamingServerMethod<TRequest, TResponse> handler)
        {
#if !NETSTANDARD
            ArgumentNullException.ThrowIfNull(method);
#else
            if (method is null)
                throw new ArgumentNullException(nameof(method));
#endif
            _callHandlers[method.FullName] = async (incomingCall) =>
            {
                await ProcessServerStreamingCallAsync(
                    incomingCall,
                    method,
                    handler).ConfigureAwait(false);
            };
        }

        public override void AddMethod<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            DuplexStreamingServerMethod<TRequest, TResponse> handler)
        {
#if !NETSTANDARD
            ArgumentNullException.ThrowIfNull(method);
#else
            if (method is null)
                throw new ArgumentNullException(nameof(method));
#endif
            _callHandlers[method.FullName] = async (incomingCall) =>
            {
                await ProcessDuplexStreamingCallAsync(
                    incomingCall,
                    method,
                    handler).ConfigureAwait(false);
            };
        }

#endregion
    }
}
