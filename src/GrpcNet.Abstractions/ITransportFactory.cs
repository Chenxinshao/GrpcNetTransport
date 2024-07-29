using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GrpcNet.Abstractions
{
    public interface ITransportFactory
    {
        Task<ITransportAdapter> ConnectAsync(IPEndPoint remoteEndPoint, CancellationToken cancellationToken = default);
    }
}
