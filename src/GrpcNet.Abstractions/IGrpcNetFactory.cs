namespace GrpcNet
{
    using Grpc.Core;
    using Grpc.Net.Client;
    using GrpcNet.Abstractions;
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Net;

    /// <summary>
    /// Provides methods for constructing gRPC pipe servers and clients.
    /// </summary>
    public interface IGrpcNetFactory
    {
        /// <summary>
        /// Constructs the factory without dependency injection.
        /// </summary>
        static
#if !NETSTANDARD
            virtual
#endif
            IGrpcNetFactory CreateFactoryWithoutInjection()
        {
            throw new NotImplementedException();
        }


        /// <summary>
        /// Constructs a gRPC server that offers services on the loopback adapter or local network.
        /// </summary>
        /// <typeparam name="T">The type of the gRPC server.</typeparam>
        /// <param name="instance">The instance of the gRPC server to respond to requests.</param>
        /// <param name="loopbackOnly">If true, the server listens only on the loopback interface.</param>
        /// <returns>The <see cref="IGrpcNetServer{T}"/> that wraps the gRPC server instance. Allows you to start and stop serving as needed.</returns>
        IGrpcNetServer<T> CreateNetworkServer<
#if !NETSTANDARD
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)]
#endif
            T>(
            T instance, ITransportListener listener, bool loopbackOnly = false) where T : class;


        /// <summary>
        /// Constructs a gRPC server that offers services on the loopback adapter or local network.
        /// </summary>
        /// <typeparam name="T">The type of the gRPC server.</typeparam>
        /// <param name="instance">The instance of the gRPC server to respond to requests.</param>
        /// <param name="loopbackOnly">If true, the server listens only on the loopback interface.</param>
        /// <returns>The <see cref="IGrpcNetServer{T}"/> that wraps the gRPC server instance. Allows you to start and stop serving as needed.</returns>
        IGrpcNetServer<T> CreateNetworkServer<
#if !NETSTANDARD
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] 
#endif
            T>(
            T instance, ITransportListener listener, int port, bool loopbackOnly = true) where T : class;

        IGrpcNetServer<T> CreateNetworkServer<
#if !NETSTANDARD
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] 
#endif
            T>(
            T instance, ITransportListener listener, int port, string host) where T : class;

        /// <summary>
        /// Creates a gRPC client that connects to services on the local network.
        /// </summary>
        /// <typeparam name="T">The gRPC client type.</typeparam>
        /// <param name="endpoint">The remote endpoint to connect to.</param>
        /// <param name="constructor">The callback to construct the client type using the provided channel.</param>
        /// <param name="grpcChannelOptions">Additional options to apply to the channel.</param>
        /// <returns>The constructor gRPC client.</returns>
        T CreateNetworkClient<T>(
            IPEndPoint endpoint,
            ITransportFactory transportFactory,
            Func<CallInvoker, T> constructor,
            GrpcChannelOptions? grpcChannelOptions = null);
    }
}