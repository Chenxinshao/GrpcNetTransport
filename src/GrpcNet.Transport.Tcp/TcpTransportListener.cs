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
    public class TcpTransportListener : ITransportListener
    {

        public TcpListener? _listener;
		
        public bool NoDelay { get => _listener!.Server.NoDelay; set => _listener!.Server.NoDelay = value; }

        public async Task<ITransportAdapter> AcceptAsync(CancellationToken cancellationToken = default)
        {
            var transport = await _listener.AcceptTcpClientAsync(
#if  !NETSTANDARD
                cancellationToken
#endif
                );
            return new TcpTransportAdapter(transport);
        }

        public void Dispose()
        {
#if !NETSTANDARD
            _listener?.Dispose();
#else
            _listener = null;
#endif
        }

        public ValueTask DisposeAsync()
        {
#if !NETSTANDARD
            _listener?.Dispose();
            return ValueTask.CompletedTask;
#else
            _listener = null;
            return default;
#endif
        }

        public Task ListenAsync(IPEndPoint listenEndPoint, CancellationToken cancellationToken = default)
        {
            _listener = new TcpListener(listenEndPoint);
            return Task.CompletedTask;
        }

        public void Start()
        {
            _listener?.Start();
        }

        public void Stop()
        {
            _listener?.Stop();
        }
    }
}
