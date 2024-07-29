namespace GrpcNet
{
    using Grpc.Core;
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks;

    /// <summary>
    /// Wraps a gRPC server instance, allowing you to start and stop the server.
    /// </summary>
    /// <typeparam name="T">The gRPC server type.</typeparam>
    public interface IGrpcNetServer<
#if !NETSTANDARD
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)]
#endif
        T> : IAsyncDisposable where T : class
    {
        /// <summary>
        /// Starts the gRPC server.
        /// </summary>
        /// <returns>The awaitable task.</returns>
        Task StartAsync();

        /// <summary>
        /// Stops the gRPC server.
        /// </summary>
        /// <returns>The awaitable task.</returns>
        Task StopAsync();

        /// <summary>
        /// If this server is created from <see cref="IGrpcNetFactory.CreateNetworkServer{T}(T, bool)"/>, this is the local port on which the server is listening.
        /// </summary>
        /// <exception cref="NotSupportedException">Thrown if this server is not created by <see cref="IGrpcNetFactory.CreateNetworkServer{T}(T, bool)"/>.</exception>
        int NetworkPort { get; }

        public ServiceBinderBase? ServiceBinder { get; }
    }
}