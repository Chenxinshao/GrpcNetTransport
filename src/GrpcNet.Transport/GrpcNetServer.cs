namespace GrpcNet.Transport
{
    using Microsoft.Extensions.Logging;
    using GrpcNet.Transport.Impl;
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Net.Sockets;
    using System.Net;
    using System.Threading.Tasks;
    using System.Reflection;
    using Grpc.Core;
    using GrpcNet;
    using GrpcNet.Abstractions;

    internal sealed class GrpcNetServer<
#if !NETSTANDARD
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)]
#endif
    T> : IGrpcNetServer<T>, IAsyncDisposable
        where T : class
    {
        private readonly T _instance;
        private readonly ILogger<GrpcNetServer<T>> _logger;
        private GrpcServer? _app;
        private int _networkPort;
        private string _host;
        readonly ITransportListener _listener;
        public GrpcNetServer(
            ILogger<GrpcNetServer<T>> logger,
            T instance,
            ITransportListener listener,
            int port,
            string host)
        {
            _logger = logger;
            _instance = instance;
            _listener = listener;
            _app = null;
            _networkPort = port;
            _host = host;
        }
        public GrpcNetServer(
            ILogger<GrpcNetServer<T>> logger,
            T instance,
            ITransportListener listener,
            bool loopbackOnly) : this(logger, instance, listener, 0, loopbackOnly ? IPAddress.Loopback.ToString() : IPAddress.Any.ToString())
        {
        }

        public GrpcNetServer(
            ILogger<GrpcNetServer<T>> logger,
            T instance,
            ITransportListener listener,
            int port,
            bool loopbackOnly) : this(logger, instance, listener, port, loopbackOnly ? IPAddress.Loopback.ToString() : IPAddress.Any.ToString())
        {
        }



        public async Task StartAsync()
        {
            if (_app != null)
            {
                return;
            }

            do
            {
                GrpcServer? app = null;
                try
                {
                    //GrpcPipeLog.GrpcServerStarting(_logger);

                    var endpoint = new IPEndPoint(IPAddress.Parse(_host), _networkPort);
                    await _listener.ListenAsync(endpoint).ConfigureAwait(false);
                    app = new GrpcServer(_listener, _logger);
                    var binderAttr = typeof(T).GetCustomAttribute<BindServiceMethodAttribute>();
                    if (binderAttr == null)
                    {
                        throw new InvalidOperationException($"{typeof(T).FullName} does not have the grpc::BindServiceMethod attribute.");
                    }
                    var targetMethods = binderAttr.BindType.GetMethods(
                        BindingFlags.Static |
                        BindingFlags.Public |
                        BindingFlags.FlattenHierarchy);
                    var binder = targetMethods
                        .Where(x => x.Name == binderAttr.BindMethodName && x.GetParameters().Length == 2)
                        .First();
                    binder.Invoke(null, BindingFlags.DoNotWrapExceptions, null, [app, _instance], null);

                    _app = app;
                    return;
                }
                catch (IOException ex) when ((
                    // Pointer file is open by another server.
                    ex.Message.Contains("used by another process", StringComparison.OrdinalIgnoreCase)))
                {

                    if (app != null)
                    {
                        await app.DisposeAsync().ConfigureAwait(false);
                        app = null;
                    }
                    continue;
                }
            } while (true);
        }

        public async Task StopAsync()
        {

            if (_app != null)
            {
                await _app.DisposeAsync().ConfigureAwait(false);
                _app = null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
        }

        public ServiceBinderBase? ServiceBinder => _app;

        public int NetworkPort
        {
            get
            {
                return _networkPort;
            }
        }
    }
}
