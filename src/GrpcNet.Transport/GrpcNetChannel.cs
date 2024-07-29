using Grpc.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GrpcNet.Transport
{
    public class GrpcNetChannel : ChannelBase
    {
        CallInvoker _callInvoker;
        public GrpcNetChannel(CallInvoker callInvoker):base("") {
            _callInvoker = callInvoker;
        }

        public override CallInvoker CreateCallInvoker() => _callInvoker;

        public void Dispose()
        {
        }
    }
}
