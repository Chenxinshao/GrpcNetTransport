namespace GrpcNet.Transport.Impl
{
    using Grpc.Core;
    using Google.Protobuf;
    using Google.Protobuf.Reflection;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Buffers;
    using System.Buffers.Binary;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading.Tasks;
    using GrpcNet.Abstractions;
#if NETSTANDARD
    using System.IO.Shims;
#endif
    internal class GrpcTransportConnection : IAsyncDisposable
    {
        private readonly Stream _networkStream;
        private readonly ITransportAdapter? _transport;
        private readonly EndPoint? _remoteEndpoint;
        private readonly ILogger? _logger;
        private readonly bool _isClient;
        private readonly Concurrency.Mutex _disposeMutex;
        private bool _readInterrupted;
        private bool _writeInterrupted;
        private bool _disposed;
        private readonly ITransportFactory _transportFactory;
        public static async Task<GrpcTransportConnection> ConnectAsync(
            IPEndPoint endpoint,
            ITransportFactory transportFactory,
            ILogger? logger,
            CancellationToken cancellationToken)
        {
            ITransportAdapter transport = null;
            try
            {
                if (logger != null)
                {
                    logger.LogTrace($"GrpcTransportConnection(Client): Connecting to remote endpoint '{endpoint}'...");
                }
                for (var i = 0; i < 3000; i += 100)
                {
                    try
                    {
                        transport = await transportFactory.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
                        break;
                    }
                    catch (SocketException) when (i < 3000 - 1)
                    {
                        if (logger != null)
                        {
                            logger.LogTrace($"GrpcTransportConnection(Client): Connection to '{endpoint}' failed, retrying in {i}ms...");
                        }
                        await Task.Delay(i, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (SocketException ex)
            {
                throw new RpcException(new Status(StatusCode.Unavailable, $"Unable to connect to the remote host: '{ex}'"));
            }
            return new GrpcTransportConnection(transport, logger, true);
        }

        public GrpcTransportConnection(
            ITransportAdapter? transport,
            ILogger? logger,
            bool isClient)
        {
            _disposeMutex = new Concurrency.Mutex();
            _disposed = false;
            _networkStream = transport.GetStream();
            _transport = transport;
            _remoteEndpoint = transport.RemoteEndPoint;
            _logger = logger;
            _isClient = isClient;
        }

        private void LogTrace(string message)
        {
            if (_logger != null)
            {
                _logger.LogTrace($"GrpcTransportConnection({(_isClient ? "Client" : "Server")}): '{_remoteEndpoint}': {message}");
            }
        }

        public bool HasWriteBeenInterrupted => _writeInterrupted;

        public bool HasReadBeenInterrupted => _readInterrupted;

        private enum OpMode
        {
            Read,
            Write,
        }

        private async Task<T> OpWithExceptionHandling<T>(OpMode mode, Func<Task<T>> execute)
        {
            try
            {
                return await execute().ConfigureAwait(false);
            }
            catch (IOException ex) when (
                ex.InnerException is SocketException se &&
                (se.SocketErrorCode == SocketError.ConnectionAborted ||
                 se.SocketErrorCode == SocketError.ConnectionReset))
            {
                throw new RpcException(new Status(StatusCode.Unavailable, "The remote host closed the connection."));
            }
            catch (EndOfStreamException)
            {
                throw new RpcException(new Status(StatusCode.Unavailable, "The remote host closed the connection."));
            }
            catch (OperationCanceledException)
            {
                LogTrace($"{(mode == OpMode.Read ? "Read" : "Write")} has been interrupted due to OperationCanceledException.");
                if (mode == OpMode.Read)
                {
                    _readInterrupted = true;
                }
                else
                {
                    _writeInterrupted = true;
                }
                throw;
            }
            catch (Exception ex)
            {
                throw new RpcException(new Status(StatusCode.Internal, $"Unhandled exception in GrpcTransportConnection: {ex}"));
            }
        }

        public async Task WriteAsync<T>(T value, CancellationToken cancellationToken) where T : IMessage
        {
            GrpcTransportInterruptedException.ThrowIf(_writeInterrupted);

            await OpWithExceptionHandling(OpMode.Write, async () =>
            {
                var length = value.CalculateSize();
                //var lengthBuffer = new byte[sizeof(int)];
                var lengthBuffer = ArrayPool<byte>.Shared.Rent(sizeof(int));
                using var dataBuffer = MemoryPool<byte>.Shared.Rent(length);
                BinaryPrimitives.WriteInt32BigEndian(lengthBuffer, length);
                value.WriteTo(dataBuffer.Memory.Span.Slice(0, length));
                LogTrace($"Start write of {typeof(T).Name} with length {length}.");
                await _networkStream.WriteAsync(lengthBuffer, cancellationToken).ConfigureAwait(false);
                await _networkStream.WriteAsync(dataBuffer.Memory.Slice(0, length), cancellationToken).ConfigureAwait(false);
                LogTrace($"End write of {typeof(T).Name} with length {length}.");
                ArrayPool<byte>.Shared.Return(lengthBuffer);
                return 0;
            }).ConfigureAwait(false);
        }

        public async Task WriteBlobAsync(byte[] value, CancellationToken cancellationToken)
        {
            GrpcTransportInterruptedException.ThrowIf(_writeInterrupted);

            await OpWithExceptionHandling(OpMode.Write, async () =>
            {
                var length = value.Length;
                //var lengthBuffer = new byte[sizeof(int)];
                var lengthBuffer = ArrayPool<byte>.Shared.Rent(sizeof(int));
                BinaryPrimitives.WriteInt32BigEndian(lengthBuffer, length);
                LogTrace($"Start write of blob with length {length}.");
                await _networkStream.WriteAsync(lengthBuffer, cancellationToken).ConfigureAwait(false);
                await _networkStream.WriteAsync(value, cancellationToken).ConfigureAwait(false);
                LogTrace($"End write of blob with length {length}.");
                ArrayPool<byte>.Shared.Return(lengthBuffer);
                return 0;
            }).ConfigureAwait(false);
        }

        public async Task<T> ReadExpectedAsync<T>(
            MessageDescriptor descriptor,
            CancellationToken cancellationToken) where T : IMessage
        {
            GrpcTransportInterruptedException.ThrowIf(_readInterrupted);

            return await OpWithExceptionHandling(OpMode.Read, async () =>
            {
                //var lengthBuffer = new byte[sizeof(int)];
                var lengthBuffer = ArrayPool<byte>.Shared.Rent(sizeof(int));
                await _networkStream.ReadExactlyAsync(lengthBuffer, cancellationToken).ConfigureAwait(false);
                var length = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);
                LogTrace($"Start read of {typeof(T).Name} with length {length}.");
                using var dataBuffer = MemoryPool<byte>.Shared.Rent(length);
                await _networkStream.ReadExactlyAsync(dataBuffer.Memory.Slice(0, length), cancellationToken).ConfigureAwait(false);
                LogTrace($"End read of {typeof(T).Name} with length {length}.");
                ArrayPool<byte>.Shared.Return(lengthBuffer);
                return (T)descriptor.Parser.ParseFrom(dataBuffer.Memory.Span.Slice(0, length));
            }).ConfigureAwait(false);
        }

        private class NestedMemoryOwner : IMemoryOwner<byte>
        {
            private readonly IMemoryOwner<byte> _parent;
            private readonly int _length;
            private readonly Memory<byte> _slice;

            public NestedMemoryOwner(IMemoryOwner<byte> parent, int length)
            {
                _parent = parent;
                _length = length;
                _slice = _parent.Memory.Slice(0, _length);
            }

            public Memory<byte> Memory => _slice;

            public void Dispose()
            {
                _parent.Dispose();
            }
        }

        public async Task<IMemoryOwner<byte>> ReadBlobAsync(
            CancellationToken cancellationToken)
        {
            GrpcTransportInterruptedException.ThrowIf(_readInterrupted);

            return await OpWithExceptionHandling(OpMode.Read, async () =>
            {
                //var lengthBuffer = new byte[sizeof(int)];
                var lengthBuffer = ArrayPool<byte>.Shared.Rent(sizeof(int));
                await _networkStream.ReadExactlyAsync(lengthBuffer, cancellationToken).ConfigureAwait(false);
                var length = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);
                LogTrace($"Start read of blob with length {length}.");
                var dataBuffer = MemoryPool<byte>.Shared.Rent(length);
                try
                {
                    await _networkStream.ReadExactlyAsync(
                        dataBuffer.Memory.Slice(0, length),
                        cancellationToken).ConfigureAwait(false);
                    LogTrace($"End read of blob with length {length}.");
                    return new NestedMemoryOwner(dataBuffer, length);
                }
                catch
                {
                    dataBuffer.Dispose();
                    throw;
                }
                ArrayPool<byte>.Shared.Return(lengthBuffer);
            }).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            using (await _disposeMutex.WaitAsync(CancellationToken.None).ConfigureAwait(false))
            {
                if (_disposed)
                {
                    return;
                }

                LogTrace($"Flushing network stream before disposal.");
                try
                {
                    using var flushCts = new CancellationTokenSource(5000);
                    await _networkStream.FlushAsync(flushCts.Token).ConfigureAwait(false);
                }
                catch
                {
                }

                LogTrace($"Start dispose of gRPC connection.");
                await _networkStream.DisposeAsync().ConfigureAwait(false);
                _transport.Close();
                _transport.Dispose();
                _disposed = true;
                LogTrace($"End dispose of gRPC connection.");
            }
        }
    }
}
