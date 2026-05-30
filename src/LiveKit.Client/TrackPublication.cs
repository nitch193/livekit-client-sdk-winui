using System;
using System.Threading;
using LiveKit.Internal;

namespace LiveKit
{
    /// <summary>
    /// Represents a published track in a LiveKit room.
    /// </summary>
    public class TrackPublication : IDisposable
    {
        private readonly FfiHandle _handle;
        private int _disposed;

        internal TrackPublication(ulong asyncId, LiveKit.Proto.TrackPublicationInfo info, FfiHandle handle)
        {
            AsyncId = asyncId;
            Info = info;
            _handle = handle;
        }

        public ulong AsyncId { get; }
        public LiveKit.Proto.TrackPublicationInfo Info { get; }

        public FfiHandle Handle => _handle;

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            {
                _handle?.Dispose();
            }
        }
    }
}
