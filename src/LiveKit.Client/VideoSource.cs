using System;
using System.Threading;
using LiveKit.Internal;
using LiveKit.Proto;

namespace LiveKit
{
    /// <summary>
    /// Represents a native video source that frames are pushed into.
    ///
    /// The native handle is stored as a <see cref="FfiHandle"/> (SafeHandle) rather
    /// than a raw <c>ulong</c>. This means:
    ///   • The CLR's CER-backed finalizer calls <c>FfiDropHandle</c> even if the
    ///     managed Dispose() is never reached (e.g. exception, GC collection).
    ///   • Double-dispose is impossible — SafeHandle's internal reference count
    ///     prevents a second ReleaseHandle call.
    ///   • Thread-safe dispose: Interlocked.Exchange on <c>_disposed</c> ensures
    ///     that concurrent Dispose() calls are both safe and idempotent.
    /// </summary>
    public class VideoSource : IDisposable
    {
        private readonly FfiHandle _handle;
        private int _disposed; // 0 = alive, 1 = disposed (Interlocked)

        internal VideoSource(FfiHandle handle)
        {
            _handle = handle ?? throw new ArgumentNullException(nameof(handle));
        }

        /// <summary>
        /// Creates a new native video source at the given resolution.
        /// </summary>
        public static VideoSource Create(uint width, uint height)
        {
            var client = FfiClient.Instance;

            var request = new FfiRequest
            {
                NewVideoSource = new NewVideoSourceRequest
                {
                    Type = VideoSourceType.VideoSourceNative,
                    Resolution = new VideoSourceResolution
                    {
                        Width  = width,
                        Height = height
                    }
                }
            };

            var response = client.SendRequest(request);

            var ownedHandle = response.NewVideoSource?.Source?.Handle;
            if (ownedHandle == null || ownedHandle.Id == 0)
                throw new Exception("Failed to create video source: native returned a null handle");

            return new VideoSource(FfiHandle.FromOwnedHandle(ownedHandle));
        }

        /// <summary>
        /// The raw handle id — use only to embed in protobuf request fields.
        /// Keep this VideoSource alive for the duration of the native call.
        /// </summary>
        public ulong Handle => (ulong)_handle.DangerousGetHandle();

        /// <summary>
        /// Pushes a raw video frame into this source.
        /// </summary>
        public void CaptureFrame(
            VideoBufferInfo buffer,
            long timestampUs,
            VideoRotation rotation = VideoRotation._0)
        {
            if (Volatile.Read(ref _disposed) == 1)
                throw new ObjectDisposedException(nameof(VideoSource));

            var request = new FfiRequest
            {
                CaptureVideoFrame = new CaptureVideoFrameRequest
                {
                    SourceHandle = Handle,
                    Buffer       = buffer,
                    TimestampUs  = timestampUs,
                    Rotation     = rotation
                }
            };

            FfiClient.Instance.SendRequest(request);
        }

        /// <summary>
        /// Releases the native video source.
        /// SafeHandle's finalizer guarantees release even if this is never called.
        /// </summary>
        public void Dispose()
        {
            // Interlocked.Exchange ensures only the first caller proceeds.
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _handle.Dispose(); // tells SafeHandle to run ReleaseHandle
            }
        }
    }
}
