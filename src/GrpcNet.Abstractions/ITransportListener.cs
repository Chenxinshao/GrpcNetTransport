using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GrpcNet.Abstractions
{
    public interface ITransportListener : IAsyncDisposable
    {
        bool NoDelay { get; set; }

        Task ListenAsync(IPEndPoint listenEndPoint, CancellationToken cancellationToken = default);

        Task<ITransportAdapter> AcceptAsync(CancellationToken cancellationToken = default);
        void Dispose();
        void Start();
        void Stop();
    }
}
