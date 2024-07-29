namespace GrpcNet.Transport.Impl
{
    using Grpc.Core;
    using System;
    using System.Buffers;

    internal class GrpcDeserializationContext : DeserializationContext
    {
        private readonly Memory<byte> _memory;

        public GrpcDeserializationContext(Memory<byte> memory)
        {
            _memory = memory;
        }

        public override byte[] PayloadAsNewBuffer()
        {
            return _memory.ToArray();
        }

        public override ReadOnlySequence<byte> PayloadAsReadOnlySequence()
        {
            return new ReadOnlySequence<byte>(_memory);
        }

        public override int PayloadLength => _memory.Length;
    }
}
