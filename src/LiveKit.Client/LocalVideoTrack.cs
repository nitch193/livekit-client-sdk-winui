using System;
using System.Threading;
using LiveKit.Internal;
using LiveKit.Proto;

namespace LiveKit
{
    /// <summary>
    /// A local video track that can be published to a LiveKit room.
    ///
    /// Like <see cref="VideoSource"/>, the track handle is stored as a
    /// <see cref="FfiHandle"/> so the CLR's CER finalizer guarantees release even
    /// if the caller forgets to call Dispose().
    /// </summary>
    public class LocalVideoTrack : IDisposable
    {
        private readonly VideoSource _source;
        private readonly FfiHandle   _trackHandle;
        private int _disposed; // 0 = alive, 1 = disposed (Interlocked)

        private LocalVideoTrack(VideoSource source, FfiHandle trackHandle, string name)
        {
            _source      = source      ?? throw new ArgumentNullException(nameof(source));
            _trackHandle = trackHandle ?? throw new ArgumentNullException(nameof(trackHandle));
            Name         = name;
        }

        public string      Name        { get; }
        public VideoSource Source      => _source;

        /// <summary>
        /// The raw track handle id — embed in protobuf fields only.
        /// Keep this <see cref="LocalVideoTrack"/> alive for the duration of the call.
        /// </summary>
        public ulong TrackHandle => (ulong)_trackHandle.DangerousGetHandle();

        // ── Factory methods ──────────────────────────────────────────────────────

        /// <summary>
        /// Creates a local video track backed by an existing <see cref="VideoSource"/>.
        /// </summary>
        public static LocalVideoTrack CreateFromSource(VideoSource source, string name = "video")
        {
            var client = FfiClient.Instance;

            var request = new FfiRequest
            {
                CreateVideoTrack = new CreateVideoTrackRequest
                {
                    Name         = name,
                    SourceHandle = source.Handle
                }
            };

            var response = client.SendRequest(request);

            var ownedHandle = response.CreateVideoTrack?.Track?.Handle;
            if (ownedHandle == null || ownedHandle.Id == 0)
                throw new Exception("Failed to create video track: native returned a null handle");

            return new LocalVideoTrack(source, FfiHandle.FromOwnedHandle(ownedHandle), name);
        }

        /// <summary>
        /// Convenience factory: creates a video source and a screen-share track in
        /// one call.
        /// </summary>
        public static LocalVideoTrack CreateScreenShareTrack(uint width = 1920, uint height = 1080)
        {
            var source = VideoSource.Create(width, height);
            return CreateFromSource(source, "screen_share");
        }

        // ── Dispose ──────────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _trackHandle.Dispose(); // SafeHandle → ReleaseHandle → FfiDropHandle
                _source.Dispose();
            }
        }
    }
}
