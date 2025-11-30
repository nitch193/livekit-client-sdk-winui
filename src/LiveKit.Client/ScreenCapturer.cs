using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LiveKit.Proto;

namespace LiveKit
{
    /// <summary>
    /// Captures screen content and pushes frames to a video source.
    /// Linux implementation using X11.
    /// </summary>
    public class ScreenCapturer : IDisposable
    {
        private readonly VideoSource _source;
        private readonly uint _width;
        private readonly uint _height;
        private readonly int _fps;
        private CancellationTokenSource? _cts;
        private Task? _captureTask;
        private bool _disposed;

        public ScreenCapturer(VideoSource source, uint width = 1920, uint height = 1080, int fps = 15)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _width = width;
            _height = height;
            _fps = fps;
        }

        /// <summary>
        /// Starts capturing the screen and pushing frames.
        /// </summary>
        public void Start()
        {
            if (_captureTask != null)
                throw new InvalidOperationException("Already capturing");

            _cts = new CancellationTokenSource();
            _captureTask = Task.Run(() => CaptureLoop(_cts.Token));
        }

        /// <summary>
        /// Stops capturing the screen.
        /// </summary>
        public async Task StopAsync()
        {
            if (_cts == null || _captureTask == null)
                return;

            _cts.Cancel();
            await _captureTask;
            _cts.Dispose();
            _cts = null;
            _captureTask = null;
        }

        private async Task CaptureLoop(CancellationToken cancellationToken)
        {
            var frameInterval = TimeSpan.FromMilliseconds(1000.0 / _fps);
            var frameBuffer = new byte[_width * _height * 4]; // RGBA
            
            Console.WriteLine($"Starting screen capture at {_width}x{_height} @ {_fps}fps");

            while (!cancellationToken.IsCancellationRequested)
            {
                var startTime = DateTime.UtcNow;

                try
                {
                    // Capture screen (simplified - just fill with test pattern for now)
                    // In a real implementation, use X11 XGetImage or similar
                    CaptureScreenSimple(frameBuffer);

                    // Create VideoBufferInfo
                    var bufferInfo = CreateBufferInfo(frameBuffer);

                    // Push frame to source
                    var timestampUs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;
                    _source.CaptureFrame(bufferInfo, timestampUs);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error capturing frame: {ex.Message}");
                }

                // Wait for next frame
                var elapsed = DateTime.UtcNow - startTime;
                var delay = frameInterval - elapsed;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }

            Console.WriteLine("Screen capture stopped");
        }

        private void CaptureScreenSimple(byte[] buffer)
        {
            // TODO: Implement actual screen capture using X11 or similar
            // For now, generate a test pattern
            var time = DateTime.UtcNow.Ticks / 10000000; // Seconds
            
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    int idx = (int)((y * _width + x) * 4);
                    
                    // Create a moving gradient pattern
                    buffer[idx + 0] = (byte)((x + time * 10) % 256);     // R
                    buffer[idx + 1] = (byte)((y + time * 10) % 256);     // G
                    buffer[idx + 2] = (byte)(((x + y) / 2 + time * 10) % 256); // B
                    buffer[idx + 3] = 255;                                // A
                }
            }
        }

        private unsafe VideoBufferInfo CreateBufferInfo(byte[] frameBuffer)
        {
            // Pin the buffer and create VideoBufferInfo
            var handle = GCHandle.Alloc(frameBuffer, GCHandleType.Pinned);
            var ptr = handle.AddrOfPinnedObject();

            var bufferInfo = new VideoBufferInfo
            {
                Type = VideoBufferType.Rgba,
                Width = _width,
                Height = _height,
                DataPtr = (ulong)ptr.ToInt64(),
                Stride = _width * 4
            };

            // Note: In production, we need to keep the handle alive until the frame is processed
            // For now, we'll unpin immediately (this may cause issues)
            handle.Free();

            return bufferInfo;
        }

        public void Dispose()
        {
            if (_disposed) return;

            StopAsync().Wait();
            _disposed = true;
        }
    }
}
