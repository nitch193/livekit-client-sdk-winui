using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using LiveKit.Proto;

namespace LiveKit.Internal
{
    /// <summary>
    /// Extension helpers for stamping a client-generated <c>RequestAsyncId</c> onto
    /// outgoing FFI request objects before they cross the native boundary.
    ///
    /// The ID is generated atomically with <see cref="Interlocked.Increment"/> so
    /// every concurrent request gets a strictly unique, monotonically increasing value
    /// without any lock overhead.
    ///
    /// The property setter is discovered once per concrete request type via reflection
    /// and cached in a <see cref="ConcurrentDictionary{TKey,TValue}"/>, so the
    /// reflection cost is paid exactly once — all subsequent calls reuse the cached
    /// delegate.  This approach is safe on AOT/IL2CPP runtimes because it relies only
    /// on <see cref="PropertyInfo.SetValue"/> rather than expression compilation.
    /// </summary>
    internal static class FfiRequestExtensions
    {
        // Shared counter — Interlocked.Increment is a single CPU instruction (LOCK XADD),
        // so no lock is needed and the result is still strictly unique per call.
        private static long _nextRequestAsyncId;

        // One setter delegate per concrete protobuf request type.  GetOrAdd is atomic,
        // so two threads racing on the first request of a given type will not create
        // two separate delegates.
        private static readonly ConcurrentDictionary<Type, Action<object, ulong>?> _setterCache
            = new ConcurrentDictionary<Type, Action<object, ulong>?>();

        /// <summary>
        /// Assigns a fresh, globally unique <c>RequestAsyncId</c> to the request object
        /// (if that type exposes such a writable <c>ulong</c> property) and returns the
        /// same id so the caller can register a pending-callback entry before sending.
        ///
        /// Returns <c>0</c> when the type does not carry a <c>RequestAsyncId</c>
        /// property (fire-and-forget requests).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong InitializeRequestAsyncId<T>(this T request)
        {
            if (request == null) return 0;

            var setter = _setterCache.GetOrAdd(request.GetType(), static type =>
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
                var prop = type.GetProperty("RequestAsyncId", flags);

                if (prop == null || !prop.CanWrite || prop.PropertyType != typeof(ulong))
                    return null;

                return (target, value) => prop.SetValue(target, value);
            });

            if (setter == null) return 0;

            // Cast is safe: Interlocked.Increment returns a long; we only use the lower
            // 63 bits and ulong 0 is reserved for "no pending callback".
            var requestAsyncId = (ulong)Interlocked.Increment(ref _nextRequestAsyncId);
            setter(request, requestAsyncId);
            return requestAsyncId;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Inject<T>(this FfiRequest ffiRequest, T request)
        {
            switch (request)
            {
                case DisposeRequest disposeRequest:
                    ffiRequest.Dispose = disposeRequest;
                    break;
                // Room
                case ConnectRequest connectRequest:
                    ffiRequest.Connect = connectRequest;
                    break;
                case DisconnectRequest disconnectRequest:
                    ffiRequest.Disconnect = disconnectRequest;
                    break;
                case PublishTrackRequest publishTrackRequest:
                    ffiRequest.PublishTrack = publishTrackRequest;
                    break;
                case UnpublishTrackRequest unpublishTrackRequest:
                    ffiRequest.UnpublishTrack = unpublishTrackRequest;
                    break;
                case PublishDataRequest publishDataRequest:
                    ffiRequest.PublishData = publishDataRequest;
                    break;
                case SetSubscribedRequest setSubscribedRequest:
                    ffiRequest.SetSubscribed = setSubscribedRequest;
                    break;
                case SetLocalMetadataRequest updateLocalMetadataRequest:
                    ffiRequest.SetLocalMetadata = updateLocalMetadataRequest;
                    break;
                case SetLocalNameRequest updateLocalNameRequest:
                    ffiRequest.SetLocalName = updateLocalNameRequest;
                    break;
                case SetLocalAttributesRequest setLocalAttributesRequest:
                    ffiRequest.SetLocalAttributes = setLocalAttributesRequest;
                    break;
                case GetSessionStatsRequest getSessionStatsRequest:
                    ffiRequest.GetSessionStats = getSessionStatsRequest;
                    break;
                // Track
                case CreateVideoTrackRequest createVideoTrackRequest:
                    ffiRequest.CreateVideoTrack = createVideoTrackRequest;
                    break;
                case CreateAudioTrackRequest createAudioTrackRequest:
                    ffiRequest.CreateAudioTrack = createAudioTrackRequest;
                    break;
                case GetStatsRequest getStatsRequest:
                    ffiRequest.GetStats = getStatsRequest;
                    break;
                // Video
                case NewVideoStreamRequest newVideoStreamRequest:
                    ffiRequest.NewVideoStream = newVideoStreamRequest;
                    break;
                case NewVideoSourceRequest newVideoSourceRequest:
                    ffiRequest.NewVideoSource = newVideoSourceRequest;
                    break;
                case CaptureVideoFrameRequest captureVideoFrameRequest:
                    ffiRequest.CaptureVideoFrame = captureVideoFrameRequest;
                    break;
                case VideoConvertRequest videoConvertRequest:
                    ffiRequest.VideoConvert = videoConvertRequest;
                    break;
                // Audio
                case NewAudioStreamRequest newAudioStreamRequest:
                    ffiRequest.NewAudioStream = newAudioStreamRequest;
                    break;
                case NewAudioSourceRequest newAudioSourceRequest:
                    ffiRequest.NewAudioSource = newAudioSourceRequest;
                    break;
                case CaptureAudioFrameRequest captureAudioFrameRequest:
                    ffiRequest.CaptureAudioFrame = captureAudioFrameRequest;
                    break;
                case NewAudioResamplerRequest newAudioResamplerRequest:
                    ffiRequest.NewAudioResampler = newAudioResamplerRequest;
                    break;
                case RemixAndResampleRequest remixAndResampleRequest:
                    ffiRequest.RemixAndResample = remixAndResampleRequest;
                    break;
                case LocalTrackMuteRequest localTrackMuteRequest:
                    ffiRequest.LocalTrackMute = localTrackMuteRequest;
                    break;
                case EnableRemoteTrackRequest enableRemoteTrackRequest:
                    ffiRequest.EnableRemoteTrack = enableRemoteTrackRequest;
                    break;
                case E2eeRequest e2EeRequest:
                    ffiRequest.E2Ee = e2EeRequest;
                    break;
                // Rpc
                case RegisterRpcMethodRequest registerRpcMethodRequest:
                    ffiRequest.RegisterRpcMethod = registerRpcMethodRequest;
                    break;
                case UnregisterRpcMethodRequest unregisterRpcMethodRequest:
                    ffiRequest.UnregisterRpcMethod = unregisterRpcMethodRequest;
                    break;
                case PerformRpcRequest performRpcRequest:
                    ffiRequest.PerformRpc = performRpcRequest;
                    break;
                case RpcMethodInvocationResponseRequest rpcMethodInvocationResponseRequest:
                    ffiRequest.RpcMethodInvocationResponse = rpcMethodInvocationResponseRequest;
                    break;
                // Data stream
                case TextStreamReaderReadIncrementalRequest textStreamReaderReadIncrementalRequest:
                    ffiRequest.TextReadIncremental = textStreamReaderReadIncrementalRequest;
                    break;
                case TextStreamReaderReadAllRequest textStreamReaderReadAllRequest:
                    ffiRequest.TextReadAll = textStreamReaderReadAllRequest;
                    break;
                case ByteStreamReaderReadIncrementalRequest byteStreamReaderReadIncrementalRequest:
                    ffiRequest.ByteReadIncremental = byteStreamReaderReadIncrementalRequest;
                    break;
                case ByteStreamReaderReadAllRequest byteStreamReaderReadAllRequest:
                    ffiRequest.ByteReadAll = byteStreamReaderReadAllRequest;
                    break;
                case ByteStreamReaderWriteToFileRequest byteStreamReaderWriteToFileRequest:
                    ffiRequest.ByteWriteToFile = byteStreamReaderWriteToFileRequest;
                    break;
                case StreamSendFileRequest streamSendFileRequest:
                    ffiRequest.SendFile = streamSendFileRequest;
                    break;
                case StreamSendTextRequest streamSendTextRequest:
                    ffiRequest.SendText = streamSendTextRequest;
                    break;
                case ByteStreamOpenRequest byteStreamOpenRequest:
                    ffiRequest.ByteStreamOpen = byteStreamOpenRequest;
                    break;
                case ByteStreamWriterWriteRequest byteStreamWriterWriteRequest:
                    ffiRequest.ByteStreamWrite = byteStreamWriterWriteRequest;
                    break;
                case ByteStreamWriterCloseRequest byteStreamWriterCloseRequest:
                    ffiRequest.ByteStreamClose = byteStreamWriterCloseRequest;
                    break;
                case TextStreamOpenRequest textStreamOpenRequest:
                    ffiRequest.TextStreamOpen = textStreamOpenRequest;
                    break;
                case TextStreamWriterWriteRequest textStreamWriterWriteRequest:
                    ffiRequest.TextStreamWrite = textStreamWriterWriteRequest;
                    break;
                case TextStreamWriterCloseRequest textStreamWriterCloseRequest:
                    ffiRequest.TextStreamClose = textStreamWriterCloseRequest;
                    break;
                case SetRemoteTrackPublicationQualityRequest setRemoteTrackPublicationQualityRequest:
                    ffiRequest.SetRemoteTrackPublicationQuality = setRemoteTrackPublicationQualityRequest;
                    break;
                // Data Track
                case PublishDataTrackRequest publishDataTrackRequest:
                    ffiRequest.PublishDataTrack = publishDataTrackRequest;
                    break;
                case LocalDataTrackTryPushRequest localDataTrackTryPushRequest:
                    ffiRequest.LocalDataTrackTryPush = localDataTrackTryPushRequest;
                    break;
                case LocalDataTrackUnpublishRequest localDataTrackUnpublishRequest:
                    ffiRequest.LocalDataTrackUnpublish = localDataTrackUnpublishRequest;
                    break;
                case LocalDataTrackIsPublishedRequest localDataTrackIsPublishedRequest:
                    ffiRequest.LocalDataTrackIsPublished = localDataTrackIsPublishedRequest;
                    break;
                case SubscribeDataTrackRequest subscribeDataTrackRequest:
                    ffiRequest.SubscribeDataTrack = subscribeDataTrackRequest;
                    break;
                case RemoteDataTrackIsPublishedRequest remoteDataTrackIsPublishedRequest:
                    ffiRequest.RemoteDataTrackIsPublished = remoteDataTrackIsPublishedRequest;
                    break;
                case DataTrackStreamReadRequest dataTrackStreamReadRequest:
                    ffiRequest.DataTrackStreamRead = dataTrackStreamReadRequest;
                    break;
                default:
                    throw new Exception($"Unknown request type: {request?.GetType().FullName ?? "null"}");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnsureClean(this FfiResponse response)
        {
            if (response.MessageCase != FfiResponse.MessageOneofCase.None)
                throw new InvalidOperationException($"Response is not cleared: {response.MessageCase}");
        }
    }
}
