namespace GrpcNet.Transport.Impl
{
    using Grpc.Core;
    using Google.Protobuf;

    internal static class GrpcMetadataConverter
    {
        public static GrpcMetadata Convert(Metadata? metadata)
        {
            var result = new GrpcMetadata();
            if (metadata != null)
            {
                foreach (var entry in metadata)
                {
                    if (entry.IsBinary)
                    {
                        result.Values.Add(entry.Key, new GrpcMetadataValue { Bytes = ByteString.CopyFrom(entry.ValueBytes) });
                    }
                    else
                    {
                        result.Values.Add(entry.Key, new GrpcMetadataValue { String = entry.Value });
                    }
                }
            }
            return result;
        }

        public static Metadata Convert(GrpcMetadata metadata)
        {
            var result = new Metadata();
            foreach (var kv in metadata.Values)
            {
                switch (kv.Value.ValueCase)
                {
                    case GrpcMetadataValue.ValueOneofCase.Bytes:
                        result.Add(kv.Key, kv.Value.Bytes.ToByteArray());
                        break;
                    case GrpcMetadataValue.ValueOneofCase.String:
                        result.Add(kv.Key, kv.Value.String);
                        break;
                }
            }
            return result;
        }
    }
}
