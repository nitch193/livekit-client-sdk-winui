using System;
using LiveKit.Internal;
using LiveKit.Proto;

namespace LiveKit
{
    /// <summary>
    /// Represents a video source that can push video frames to LiveKit.
    /// </summary>
    public class VideoSource : IDisposable
    {
        private readonly FfiClient _client;
        private readonly ulong _handle;
        private bool _disposed;

        internal VideoSource(ulong handle)
        {
            _client = FfiClient.Instance;
            _handle = handle;
        }

        /// <summary>
        /// Creates a new video source with the specified resolution.
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
                        Width = width,
                        Height = height
                    }
                }
            };

            var response = client.SendRequest(request);
            
            if (response.NewVideoSource?.Source?.Handle?.Id == 0)
            {
                throw new Exception("Failed to create video source");
            }

            return new VideoSource(response.NewVideoSource.Source.Handle.Id);
        }

        /// <summary>
        /// Pushes a video frame to this source.
        /// </summary>
        public void CaptureFrame(VideoBufferInfo buffer, long timestampUs, VideoRotation rotation = VideoRotation._0)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(VideoSource));

            var request = new FfiRequest
            {
                CaptureVideoFrame = new CaptureVideoFrameRequest
                {
                    SourceHandle = _handle,
                    Buffer = buffer,
                    TimestampUs = timestampUs,
                    Rotation = rotation
                }
            };

            _client.SendRequest(request);
        }

        public ulong Handle => _handle;

        public void Dispose()
        {
            if (_disposed) return;
            
            // Drop the handle using NativeMethods
            if (_handle != 0)
            {
                try
                {
                    NativeMethods.FfiDropHandle((nint)_handle);
                }
                catch
                {
                    // Ignore errors during disposal
                }
            }

            _disposed = true;
        }
    }
}
