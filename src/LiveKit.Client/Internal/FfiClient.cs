using System;
using LiveKit.Proto;
using Google.Protobuf;


namespace LiveKit.Internal
{
    internal sealed class FfiClient : IDisposable
    {
        private static readonly Lazy<FfiClient> instance = new(() => new FfiClient());
        public static FfiClient Instance => instance.Value;

        private bool _isDisposed = false;
        private static bool initialized = false;

        public event ConnectReceivedDelegate? ConnectReceived;
        public event DisconnectReceivedDelegate? DisconnectReceived;
        public event RoomEventReceivedDelegate? RoomEventReceived;
        public event TrackEventReceivedDelegate? TrackEventReceived;
        public event PublishTrackDelegate? PublishTrackReceived;
        public event UnpublishTrackDelegate? UnpublishTrackReceived;

        private FfiClient() { }

        public void Initialize()
        {
            if (initialized) return;
            // Keep a reference to the delegate to prevent GC? 
            // Since it's a static method, it should be fine, but creating a delegate instance might be safer.
            NativeMethods.LiveKitInitialize(FFICallback, true, "csharp", "0.1.0");
            initialized = true;
            Console.WriteLine("FFI Server Initialized");
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            SendRequest(new FfiRequest { Dispose = new DisposeRequest() });
        }

        public FfiResponse SendRequest(FfiRequest request)
        {
            unsafe
            {
                int size = request.CalculateSize();
                byte[] data = new byte[size];
                request.WriteTo(data);

                fixed (byte* requestDataPtr = data)
                {
                    var handle = NativeMethods.FfiNewRequest(
                        requestDataPtr,
                        size,
                        out byte* dataPtr,
                        out UIntPtr dataLen
                    );

                    var dataSpan = new Span<byte>(dataPtr, (int)dataLen.ToUInt64());
                    var response = FfiResponse.Parser.ParseFrom(dataSpan);
                    NativeMethods.FfiDropHandle(handle);
                    return response;
                }
            }
        }

        static unsafe void FFICallback(UIntPtr data, UIntPtr size)
        {
            if (Instance._isDisposed) return;

            var respData = new Span<byte>(data.ToPointer(), (int)size.ToUInt64());
            var response = FfiEvent.Parser.ParseFrom(respData);

            switch (response.MessageCase)
            {
                case FfiEvent.MessageOneofCase.Connect:
                    Instance.ConnectReceived?.Invoke(response.Connect);
                    break;
                case FfiEvent.MessageOneofCase.Disconnect:
                    Instance.DisconnectReceived?.Invoke(response.Disconnect);
                    break;
                case FfiEvent.MessageOneofCase.RoomEvent:
                    Instance.RoomEventReceived?.Invoke(response.RoomEvent);
                    break;
                case FfiEvent.MessageOneofCase.TrackEvent:
                    Instance.TrackEventReceived?.Invoke(response.TrackEvent);
                    break;
                case FfiEvent.MessageOneofCase.PublishTrack:
                    Instance.PublishTrackReceived?.Invoke(response.PublishTrack);
                    break;
                case FfiEvent.MessageOneofCase.UnpublishTrack:
                    Instance.UnpublishTrackReceived?.Invoke(response.UnpublishTrack);
                    break;
                case FfiEvent.MessageOneofCase.Logs:
                    // Console.WriteLine("Log received");
                    break;
                default:
                    // Console.WriteLine($"Unhandled event: {response.MessageCase}");
                    break;
            }
        }
    }
}
