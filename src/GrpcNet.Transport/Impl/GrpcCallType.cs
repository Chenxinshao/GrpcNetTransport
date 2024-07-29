namespace GrpcNet.Transport.Impl
{
    internal enum GrpcCallType
    {
        Unary,
        ClientStreaming,
        ServerStreaming,
        DuplexStreaming,
    }
}
