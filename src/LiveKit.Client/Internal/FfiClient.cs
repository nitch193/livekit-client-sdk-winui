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
        public event VideoStreamEventReceivedDelegate? VideoStreamEventReceived;
        public event AudioStreamEventReceivedDelegate? AudioStreamEventReceived;
        public event RpcMethodInvocationReceivedDelegate? RpcMethodInvocationReceived;
        public event GetSessionStatsDelegate? GetSessionStatsReceived;
        public event SetLocalMetadataReceivedDelegate? SetLocalMetadataReceived;
        public event SetLocalNameReceivedDelegate? SetLocalNameReceived;
        public event SetLocalAttributesReceivedDelegate? SetLocalAttributesReceived;
        public event CaptureAudioFrameReceivedDelegate? CaptureAudioFrameReceived;
        public event PerformRpcReceivedDelegate? PerformRpcReceived;
        public event ByteStreamReaderEventReceivedDelegate? ByteStreamReaderEventReceived;
        public event ByteStreamReaderReadAllReceivedDelegate? ByteStreamReaderReadAllReceived;
        public event ByteStreamReaderWriteToFileReceivedDelegate? ByteStreamReaderWriteToFileReceived;
        public event ByteStreamOpenReceivedDelegate? ByteStreamOpenReceived;
        public event ByteStreamWriterWriteReceivedDelegate? ByteStreamWriterWriteReceived;
        public event ByteStreamWriterCloseReceivedDelegate? ByteStreamWriterCloseReceived;
        public event SendFileReceivedDelegate? SendFileReceived;
        public event TextStreamReaderEventReceivedDelegate? TextStreamReaderEventReceived;
        public event TextStreamReaderReadAllReceivedDelegate? TextStreamReaderReadAllReceived;
        public event TextStreamOpenReceivedDelegate? TextStreamOpenReceived;
        public event TextStreamWriterWriteReceivedDelegate? TextStreamWriterWriteReceived;
        public event TextStreamWriterCloseReceivedDelegate? TextStreamWriterCloseReceived;
        public event SendTextReceivedDelegate? SendTextReceived;
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
                case FfiEvent.MessageOneofCase.VideoStreamEvent:
                    Instance.VideoStreamEventReceived?.Invoke(response.VideoStreamEvent);
                    break;
                case FfiEvent.MessageOneofCase.AudioStreamEvent:
                    Instance.AudioStreamEventReceived?.Invoke(response.AudioStreamEvent);
                    break;
                case FfiEvent.MessageOneofCase.RpcMethodInvocation:
                    Instance.RpcMethodInvocationReceived?.Invoke(response.RpcMethodInvocation);
                    break;
                case FfiEvent.MessageOneofCase.GetSessionStats:
                    Instance.GetSessionStatsReceived?.Invoke(response.GetSessionStats);
                    break;
                case FfiEvent.MessageOneofCase.SetLocalMetadata:
                    Instance.SetLocalMetadataReceived?.Invoke(response.SetLocalMetadata);
                    break;
                case FfiEvent.MessageOneofCase.SetLocalName:
                    Instance.SetLocalNameReceived?.Invoke(response.SetLocalName);
                    break;
                case FfiEvent.MessageOneofCase.SetLocalAttributes:
                    Instance.SetLocalAttributesReceived?.Invoke(response.SetLocalAttributes);
                    break;
                case FfiEvent.MessageOneofCase.CaptureAudioFrame:
                    Instance.CaptureAudioFrameReceived?.Invoke(response.CaptureAudioFrame);
                    break;
                case FfiEvent.MessageOneofCase.PerformRpc:
                    Instance.PerformRpcReceived?.Invoke(response.PerformRpc);
                    break;
                case FfiEvent.MessageOneofCase.ByteStreamReaderEvent:
                    Instance.ByteStreamReaderEventReceived?.Invoke(response.ByteStreamReaderEvent);
                    break;
                case FfiEvent.MessageOneofCase.ByteStreamReaderReadAll:
                    Instance.ByteStreamReaderReadAllReceived?.Invoke(response.ByteStreamReaderReadAll);
                    break;
                case FfiEvent.MessageOneofCase.ByteStreamReaderWriteToFile:
                    Instance.ByteStreamReaderWriteToFileReceived?.Invoke(response.ByteStreamReaderWriteToFile);
                    break;
                case FfiEvent.MessageOneofCase.ByteStreamOpen:
                    Instance.ByteStreamOpenReceived?.Invoke(response.ByteStreamOpen);
                    break;
                case FfiEvent.MessageOneofCase.ByteStreamWriterWrite:
                    Instance.ByteStreamWriterWriteReceived?.Invoke(response.ByteStreamWriterWrite);
                    break;
                case FfiEvent.MessageOneofCase.ByteStreamWriterClose:
                    Instance.ByteStreamWriterCloseReceived?.Invoke(response.ByteStreamWriterClose);
                    break;
                case FfiEvent.MessageOneofCase.SendFile:
                    Instance.SendFileReceived?.Invoke(response.SendFile);
                    break;
                case FfiEvent.MessageOneofCase.TextStreamReaderEvent:
                    Instance.TextStreamReaderEventReceived?.Invoke(response.TextStreamReaderEvent);
                    break;
                case FfiEvent.MessageOneofCase.TextStreamReaderReadAll:
                    Instance.TextStreamReaderReadAllReceived?.Invoke(response.TextStreamReaderReadAll);
                    break;
                case FfiEvent.MessageOneofCase.TextStreamOpen:
                    Instance.TextStreamOpenReceived?.Invoke(response.TextStreamOpen);
                    break;
                case FfiEvent.MessageOneofCase.TextStreamWriterWrite:
                    Instance.TextStreamWriterWriteReceived?.Invoke(response.TextStreamWriterWrite);
                    break;
                case FfiEvent.MessageOneofCase.TextStreamWriterClose:
                    Instance.TextStreamWriterCloseReceived?.Invoke(response.TextStreamWriterClose);
                    break;
                case FfiEvent.MessageOneofCase.SendText:
                    Instance.SendTextReceived?.Invoke(response.SendText);
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
