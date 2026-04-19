using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using System.Diagnostics;
using System.Threading.Channels;

namespace LiveKit.Client
{
    public class Win2DVideoRenderer : IDisposable
    {
        private CanvasControl _canvasControl;
        private CanvasBitmap? _currentBitmap;
        private readonly object _bitmapLock = new object();
        private DispatcherQueue _dispatcherQueue;
        private volatile bool _disposed;

        // Store the CanvasDevice for use in render thread
        private CanvasDevice? _canvasDevice;
        private readonly object _deviceLock = new object();

        // Frame tracking for performance monitoring
        private int _frameCount = 0;
        private DateTime _lastFrameTime = DateTime.UtcNow;
        private double _currentFPS = 0;

        // Timing tracking
        private DateTime _lastRenderStartTime = DateTime.UtcNow;
        private const string RENDERER_COMPONENT = "Win2DVideoRenderer";

        // CanvasDevice availability check
        private volatile bool _deviceReady = false;

        // Bounded buffer for frame smoothing (2 frames max)
        // This smooths out burst delivery while maintaining real-time display
        private readonly Channel<VideoFrame> _frameChannel;
        private const int MAX_BUFFERED_FRAMES = 2;

        // Render thread
        private Thread? _renderThread;
        private readonly CancellationTokenSource _renderCts = new CancellationTokenSource();

        // Reusable buffer for color channel swapping
        private byte[]? _swapBuffer;

        // Frame timing for smooth display
        private readonly Stopwatch _frameStopwatch = new Stopwatch();
        private const int TARGET_FRAME_TIME_MS = 33; // ~30 FPS target
        private const int MIN_FRAME_TIME_MS = 16;    // ~60 FPS max

        public Win2DVideoRenderer(CanvasControl canvasControl)
        {
            _canvasControl = canvasControl;
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            // Create bounded channel with drop-oldest policy
            var channelOptions = new BoundedChannelOptions(MAX_BUFFERED_FRAMES)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            };
            _frameChannel = Channel.CreateBounded<VideoFrame>(channelOptions);

            // Start render thread
            _renderThread = new Thread(() => RenderLoop(_renderCts.Token))
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _renderThread.Start();
        }

        // Render loop running on separate thread
        private void RenderLoop(CancellationToken token)
        {
            //PerformanceLogger.Log(RENDERER_COMPONENT, "Render loop started");
            _frameStopwatch.Start();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Wait for frame with small timeout
                    if (_frameChannel.Reader.TryRead(out var frame))
                    {
                        if (_disposed) break;

                        // Get the device
                        CanvasDevice? device;
                        lock (_deviceLock)
                        {
                            device = _canvasDevice;
                        }

                        if (device == null)
                        {
                            continue;
                        }

                        var renderStartTime = DateTime.UtcNow;
                        var timeSinceLastRender = (renderStartTime - _lastRenderStartTime).TotalMilliseconds;
                        _lastRenderStartTime = renderStartTime;

                        // Log if there's a long gap between renders
                        if (timeSinceLastRender > 100)
                        {
                            //PerformanceLogger.Log(RENDERER_COMPONENT, $"Long gap between renders: {timeSinceLastRender:F1}ms (frame size: {frame.Width}x{frame.Height})");
                        }

                        try
                        {
                            // Render the frame
                            lock (_bitmapLock)
                            {
                                if (_disposed) return;

                                // Create or update bitmap on GPU
                                if (_currentBitmap == null ||
                                    Math.Abs(_currentBitmap.Size.Width - frame.Width) > 1 ||
                                    Math.Abs(_currentBitmap.Size.Height - frame.Height) > 1)
                                {
                                    //PerformanceLogger.Log(RENDERER_COMPONENT, $"Creating new bitmap: {frame.Width}x{frame.Height}");
                                    _currentBitmap?.Dispose();
                                    _currentBitmap = CanvasBitmap.CreateFromBytes(
                                        device,
                                        frame.Data,
                                        frame.Width,
                                        frame.Height,
                                        Windows.Graphics.DirectX.DirectXPixelFormat.R8G8B8A8UIntNormalized);
                                }
                                else
                                {
                                    _currentBitmap.SetPixelBytes(frame.Data);
                                }
                            }

                            // Invalidate canvas on UI thread
                            _dispatcherQueue.TryEnqueue(() =>
                            {
                                if (!_disposed)
                                {
                                    _canvasControl.Invalidate();
                                }
                            });

                            // Track FPS
                            TrackFrameRate();
                        }
                        catch (Exception ex)
                        {
                            //PerformanceLogger.Log(RENDERER_COMPONENT, $"Error rendering frame: {ex.Message}");
                        }
                    }
                    else
                    {
                        // No frame available, small sleep to prevent busy waiting
                        Thread.Sleep(1);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    //PerformanceLogger.Log(RENDERER_COMPONENT, $"Render loop error: {ex.Message}");
                }
            }

            //PerformanceLogger.Log(RENDERER_COMPONENT, "Render loop stopped");
        }

        // Called when GPU resources should be created.
        public void OnCreateResources(CanvasControl sender, object args)
        {
            //PerformanceLogger.Log(RENDERER_COMPONENT, "GPU resources created");

            lock (_deviceLock)
            {
                _canvasDevice = sender.Device;
            }

            _deviceReady = true;
        }

        // Called when the canvas needs to be redrawn.
        public void OnDraw(CanvasControl sender, object args)
        {
            CanvasDrawEventArgs drawArgs = (CanvasDrawEventArgs)args;

            lock (_bitmapLock)
            {
                if (_currentBitmap == null)
                {
                    return;
                }

                // Calculate aspect-ratio-preserving draw rectangle
                double canvasWidth = _canvasControl.ActualWidth;
                double canvasHeight = _canvasControl.ActualHeight;
                double bitmapWidth = _currentBitmap.Size.Width;
                double bitmapHeight = _currentBitmap.Size.Height;

                double scaleX = canvasWidth / bitmapWidth;
                double scaleY = canvasHeight / bitmapHeight;
                double scale = Math.Min(scaleX, scaleY);

                double scaledWidth = bitmapWidth * scale;
                double scaledHeight = bitmapHeight * scale;
                double offsetX = (canvasWidth - scaledWidth) / 2.0;
                double offsetY = (canvasHeight - scaledHeight) / 2.0;

                var drawRect = new Windows.Foundation.Rect(offsetX, offsetY, scaledWidth, scaledHeight);
                var sourceRect = new Windows.Foundation.Rect(0, 0, bitmapWidth, bitmapHeight);

                drawArgs.DrawingSession.DrawImage(
                    _currentBitmap,
                    drawRect,
                    sourceRect,
                    1.0f,
                    CanvasImageInterpolation.Linear);
            }
        }

        // Updates the current video frame with new data.
        public void UpdateFrame(byte[] frameData, int width, int height)
        {
            if (_disposed) return;

            // Create the new frame
            var frame = new VideoFrame
            {
                Data = frameData,
                Width = width,
                Height = height,
                Timestamp = DateTime.UtcNow.Ticks
            };

            if (_frameChannel.Writer.TryWrite(frame))
            {
            }
        }

        private void TrackFrameRate()
        {
            _frameCount++;
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastFrameTime).TotalSeconds;

            if (elapsed >= 1.0)
            {
                _currentFPS = _frameCount / elapsed;
                _frameCount = 0;
                _lastFrameTime = now;

            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Cancel and wait for render thread
            _renderCts.Cancel();
            _renderThread?.Join(1000);

            // Complete the channel
            _frameChannel.Writer.Complete();

            lock (_bitmapLock)
            {
                try
                {
                    _currentBitmap?.Dispose();
                    _currentBitmap = null;
                }
                catch (Exception ex)
                {
                }
            }

            lock (_deviceLock)
            {
                _canvasDevice = null;
            }

            _deviceReady = false;
            _renderCts.Dispose();
        }
    }

    // Video frame data container
    public class VideoFrame
    {
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public int Width { get; set; }
        public int Height { get; set; }
        public long Timestamp { get; set; }
    }
}