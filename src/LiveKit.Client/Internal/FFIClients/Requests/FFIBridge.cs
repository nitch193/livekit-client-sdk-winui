using System;
using LiveKit.Internal.FFIClients.Pools;

namespace LiveKit.Internal.FFIClients.Requests
{
    /// <summary>
    /// Standard bridge implementation for creating pooled FFI requests.
    /// </summary>
    public class FFIBridge : IFFIBridge
    {
        private static readonly Lazy<FFIBridge> _instance = new(() =>
            new FFIBridge(
                LiveKit.Internal.FfiClient.Instance,
                new ThreadSafeMultiPool()
            )
        );

        public static FFIBridge Instance => _instance.Value;

        private readonly IFFIClient _ffiClient;
        private readonly IMultiPool _multiPool;

        public FFIBridge(IFFIClient client, IMultiPool multiPool)
        {
            _ffiClient = client;
            _multiPool = multiPool;
        }

        public FfiRequestWrap<T> NewRequest<T>() where T : class, new()
        {
            return new FfiRequestWrap<T>(_ffiClient, _multiPool);
        }
    }
}
