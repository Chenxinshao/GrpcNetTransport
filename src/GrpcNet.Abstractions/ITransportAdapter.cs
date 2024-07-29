using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace GrpcNet.Abstractions
{
    public interface ITransportAdapter
    {
        EndPoint? RemoteEndPoint { get; }
        void Close();
        void Dispose();
        Stream? GetStream();
    }
}
