namespace GrpcNet.Transport.Impl
{
    using Grpc.Core;
    using System;
    using Concurrency;

    internal readonly record struct GrpcServerIncomingCall
    {
        public required GrpcRequest Request { get; init; }
        public required GrpcTransportConnection Connection { get; init; }
        public required string Peer { get; init; }
        public required Action<string> LogTrace { get; init; }

        public GrpcServerCallContext CreateCallContext(
            string methodName,
            CancellationToken cancellationToken)
        {
            return new GrpcServerCallContext(
                methodName,
                string.Empty,
                Peer,
                Request.DeadlineUnixTimeMilliseconds == 0
                    ? null
                    : DateTimeOffset.FromUnixTimeMilliseconds(Request.DeadlineUnixTimeMilliseconds).UtcDateTime,
                Request.HasRequestHeaders
                    ? GrpcMetadataConverter.Convert(Request.RequestHeaders)
                    : new Metadata(),
                Connection,
                new Mutex(),
                cancellationToken);
        }
    }
}
