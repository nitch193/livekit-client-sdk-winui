using System;
using System.Threading;
using LiveKit.Internal;
using LiveKit.Proto;

namespace LiveKit.Client
{
    /// <summary>
    /// Managed wrapper around a native video frame buffer handle.
    /// Ensures the unmanaged native resources are freed cleanly when disposed or collected.
    /// </summary>
    public class VideoFrameBuffer : IDisposable
    {
        private readonly FfiHandle _handle;
        private int _disposed;

        public FfiHandle Handle => _handle;
        public VideoBufferInfo Info { get; }
        public long TimestampUs { get; }
        public int Rotation { get; }

        internal VideoFrameBuffer(FfiHandle handle, VideoBufferInfo info, long timestampUs, int rotation)
        {
            _handle = handle;
            Info = info;
            TimestampUs = timestampUs;
            Rotation = rotation;
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            {
                _handle?.Dispose();
            }
        }
    }
}
