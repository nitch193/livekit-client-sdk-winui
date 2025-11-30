using System;
using LiveKit.Proto;

namespace LiveKit
{
    /// <summary>
    /// Represents a local video track that can be published to a LiveKit room.
    /// </summary>
    public class LocalVideoTrack : IDisposable
    {
        private readonly VideoSource _source;
        private readonly ulong _trackHandle;
        private bool _disposed;

        private LocalVideoTrack(VideoSource source, ulong trackHandle, string name = "screen")
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _trackHandle = trackHandle;
            Name = name;
        }

        public string Name { get; }
        
        public VideoSource Source => _source;
        
        public ulong TrackHandle => _trackHandle;

        /// <summary>
        /// Creates a local video track from a video source.
        /// </summary>
        public static LocalVideoTrack CreateFromSource(VideoSource source, string name = "video")
        {
            var client = Internal.FfiClient.Instance;
            
            var request = new FfiRequest
            {
                CreateVideoTrack = new CreateVideoTrackRequest
                {
                    Name = name,
                    SourceHandle = source.Handle
                }
            };

            var response = client.SendRequest(request);
            
            if (response.CreateVideoTrack?.Track?.Handle?.Id == 0)
            {
                throw new Exception("Failed to create video track");
            }

            return new LocalVideoTrack(source, response.CreateVideoTrack.Track.Handle.Id, name);
        }

        /// <summary>
        /// Creates a screen share track with the specified resolution.
        /// </summary>
        public static LocalVideoTrack CreateScreenShareTrack(uint width = 1920, uint height = 1080)
        {
            var source = VideoSource.Create(width, height);
            return CreateFromSource(source, "screen_share");
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _source?.Dispose();
            _disposed = true;
        }
    }
}
