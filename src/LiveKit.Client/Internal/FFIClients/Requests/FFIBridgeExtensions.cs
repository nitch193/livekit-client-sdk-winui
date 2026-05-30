using LiveKit.Proto;

namespace LiveKit.Internal.FFIClients.Requests
{
    /// <summary>
    /// Convenience extension methods for common FFI bridge operations.
    /// </summary>
    public static class FFIBridgeExtensions
    {
        public static (FfiResponseWrap response, ulong requestAsyncId) SendConnectRequest(
            this IFFIBridge ffiBridge, string url, string authToken, RoomOptions? roomOptions = null)
        {
            using var request = ffiBridge.NewRequest<ConnectRequest>();
            var connect = request.request;
            connect.Url = url;
            connect.Token = authToken;
            if (roomOptions != null)
                connect.Options = roomOptions;
            var response = request.Send();
            return (response, request.RequestAsyncId);
        }

        public static (FfiResponseWrap response, ulong requestAsyncId) SendDisconnectRequest(
            this IFFIBridge ffiBridge, ulong roomHandle)
        {
            using var request = ffiBridge.NewRequest<DisconnectRequest>();
            var disconnect = request.request;
            disconnect.RoomHandle = roomHandle;
            var response = request.Send();
            return (response, request.RequestAsyncId);
        }
    }
}
