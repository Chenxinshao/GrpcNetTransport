namespace GrpcNet.Transport.Impl
{
    using Grpc.Core;
    using GrpcNet.Abstractions;
    using Microsoft.Extensions.Logging;
    using System.Net;

    internal sealed class GrpcClientCallInvoker : CallInvoker
    {
        private readonly IPEndPoint _endpoint;
        private readonly ILogger? _logger;
        private readonly ITransportFactory _transportFactory;
        public GrpcClientCallInvoker(IPEndPoint endpoint, ITransportFactory transportFactory, ILogger? logger = null)
        {
            _endpoint = endpoint;
            _transportFactory = transportFactory;
            _logger = logger;
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request)
        {
            throw new NotSupportedException("Blocking unary calls are not supported by GrpcClientCallInvoker.");
        }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request)
        {
            var call = new GrpcClientCall<TRequest, TResponse>(
                _endpoint,
                _transportFactory,
                _logger,
                method,
                options,
                request,
                GrpcCallType.Unary);
            return new AsyncUnaryCall<TResponse>(
                call.GetResponseAsync(),
                call.GetResponseHeadersAsync(),
                call.GetStatus,
                call.GetTrailers,
                call.Dispose);
        }

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options)
        {
            var call = new GrpcClientCall<TRequest, TResponse>(
                _endpoint,
                _transportFactory,
                _logger,
                method,
                options,
                null,
                GrpcCallType.ClientStreaming);
            return new AsyncClientStreamingCall<TRequest, TResponse>(
                call,
                call.GetResponseAsync(),
                call.GetResponseHeadersAsync(),
                call.GetStatus,
                call.GetTrailers,
                call.Dispose);
        }

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request)
        {
            var call = new GrpcClientCall<TRequest, TResponse>(
                _endpoint,
                _transportFactory,
                _logger,
                method,
                options,
                request,
                GrpcCallType.ServerStreaming);
            return new AsyncServerStreamingCall<TResponse>(
                call,
                call.GetResponseHeadersAsync(),
                call.GetStatus,
                call.GetTrailers,
                call.Dispose);
        }

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options)
        {
            var call = new GrpcClientCall<TRequest, TResponse>(
                _endpoint,
                _transportFactory,
                _logger,
                method,
                options,
                null,
                GrpcCallType.DuplexStreaming);
            return new AsyncDuplexStreamingCall<TRequest, TResponse>(
                call,
                call,
                call.GetResponseHeadersAsync(),
                call.GetStatus,
                call.GetTrailers,
                call.Dispose);
        }
    }
}