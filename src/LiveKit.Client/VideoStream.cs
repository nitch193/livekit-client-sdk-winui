using System;
using System.Threading;
using LiveKit.Internal;
using LiveKit.Proto;
using LiveKit.Internal.FFIClients;
using LiveKit.Internal.FFIClients.Requests;

namespace LiveKit.Client
{
    /// <summary>
    /// Coordinates video stream consumption, offering high-level Start/Stop controls
    /// and implementing thread-safe latest-frame-wins coalescing to mitigate GC allocations
    /// and resolve use-after-free conditions.
    /// </summary>
    public class VideoStream : IDisposable
    {
        public delegate void FrameReceiveDelegate(VideoStream videoStream, VideoFrameBuffer buffer);

        private readonly FfiHandle _handle;
        private readonly FfiClient _client;
        private bool _disposed;
        private bool _playing;
        private bool _dirty;
        private readonly object _frameLock = new();
        private VideoFrameBuffer? _pendingBuffer;
        private VideoFrameBuffer? _activeBuffer;

        public event FrameReceiveDelegate? FrameReceived;

        public FfiHandle Handle => _handle;
        public VideoFrameBuffer? ActiveBuffer => _activeBuffer;

        public VideoStream(ulong trackHandle)
        {
            _client = FfiClient.Instance;

            using var request = FFIBridge.Instance.NewRequest<NewVideoStreamRequest>();
            var newVideoStream = request.request;
            newVideoStream.TrackHandle = trackHandle;
            newVideoStream.Type = VideoStreamType.VideoStreamNative;
            newVideoStream.Format = VideoBufferType.Rgba; // RGBA matches WinUI renderers expectation natively
            newVideoStream.NormalizeStride = true;

            using var response = request.Send();
            FfiResponse res = response;
            _handle = FfiHandle.FromOwnedHandle(res.NewVideoStream.Stream.Handle);
            
            _client.VideoStreamEventReceived += OnVideoStreamEvent;
            _playing = true;
        }

        public void Start()
        {
            lock (_frameLock)
            {
                if (_disposed) return;
                _playing = true;
            }
        }

        public void Stop()
        {
            lock (_frameLock)
            {
                _playing = false;
                _pendingBuffer?.Dispose();
                _pendingBuffer = null;
                _activeBuffer?.Dispose();
                _activeBuffer = null;
                _dirty = false;
            }
        }

        /// <summary>
        /// Consumes the latest coalesced frame. Disposes the previous active buffer.
        /// Returns the new active VideoFrameBuffer, or null if no new frame is available.
        /// </summary>
        public VideoFrameBuffer? Update()
        {
            if (_disposed || !_playing) return null;

            VideoFrameBuffer? nextBuffer = null;
            lock (_frameLock)
            {
                if (_dirty)
                {
                    nextBuffer = _pendingBuffer;
                    _pendingBuffer = null;
                    _dirty = false;
                }
            }

            if (nextBuffer != null)
            {
                _activeBuffer?.Dispose();
                _activeBuffer = nextBuffer;
            }

            return _activeBuffer;
        }

        private void OnVideoStreamEvent(VideoStreamEvent e)
        {
            if (_disposed) return;

            var streamHandleId = _handle.DangerousGetHandle();
            if (e.StreamHandle != (ulong)streamHandleId.ToInt64())
                return;

            if (e.MessageCase != VideoStreamEvent.MessageOneofCase.FrameReceived)
                return;

            var newBuffer = e.FrameReceived.Buffer;
            var frameHandle = FfiHandle.FromOwnedHandle(newBuffer.Handle);
            var frameInfo = newBuffer.Info;

            var buffer = new VideoFrameBuffer(frameHandle, frameInfo, e.FrameReceived.TimestampUs, e.FrameReceived.Rotation);

            lock (_frameLock)
            {
                if (_disposed || !_playing)
                {
                    buffer.Dispose();
                    return;
                }

                // Coalesce: drop the previous pending frame immediately to free its native resources
                _pendingBuffer?.Dispose();
                _pendingBuffer = buffer;
                _dirty = true;
            }

            FrameReceived?.Invoke(this, buffer);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _client.VideoStreamEventReceived -= OnVideoStreamEvent;

            lock (_frameLock)
            {
                _pendingBuffer?.Dispose();
                _pendingBuffer = null;
                _activeBuffer?.Dispose();
                _activeBuffer = null;
            }

            _handle.Dispose();
        }
    }
}
