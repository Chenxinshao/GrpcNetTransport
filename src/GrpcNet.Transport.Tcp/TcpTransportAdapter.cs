using GrpcNet.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace GrpcNet.Transport.Tcp
{
    public class TcpTransportAdapter : ITransportAdapter
    {
        private readonly TcpClient _tcp;
        public TcpTransportAdapter(TcpClient tcp)
        {
            _tcp = tcp;
        }
        public EndPoint? RemoteEndPoint => _tcp.Client.RemoteEndPoint;

        public void Close()
        {
            _tcp?.Close();
        }

        public void Dispose()
        {
            _tcp?.Dispose();
        }

        public Stream? GetStream()
        {
            return _tcp.GetStream();
        }
    }
}
