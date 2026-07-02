using LiveKit.Proto;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace LiveKit.Client
{
    public class ScreenCapturer : IDisposable
    {
        private readonly VideoSource _source;
        private readonly uint _width;
        private readonly uint _height;

        // Capture objects
        private GraphicsCaptureItem? _item;
        private Direct3D11CaptureFramePool? _framePool;
        private GraphicsCaptureSession? _session;

        // D3D objects
        private Vortice.Direct3D11.ID3D11Device? _d3dDevice;
        private Vortice.Direct3D11.ID3D11DeviceContext? _d3dContext;
        private readonly object _d3dLock = new(); // Guard d3dContext access across threads

        // Staging texture pool — pre-allocate N textures to avoid recreation
        private const int StagingPoolSize = 3;
        private StagingEntry[]? _stagingPool;
        private int _stagingIndex = 0;

        private IDirect3DDevice? _device;
        private object? _dispatcherQueueController;

        // Async processing queue — decouples capture from send
        private readonly BlockingCollection<PendingFrame> _frameQueue =
            new(boundedCapacity: 4); // Drop frames if sender can't keep up
        private Thread? _processingThread;
        private volatile bool _disposed;

        // FPS throttle — cap at target to avoid unnecessary CPU/GPU load
        private const double TargetFps = 30.0;
        private readonly long _frameIntervalTicks =
            (long)(Stopwatch.Frequency / TargetFps);
        private long _lastFrameTick = 0;

        private struct StagingEntry
        {
            public Vortice.Direct3D11.ID3D11Texture2D Texture;
            public uint Width;
            public uint Height;
        }

        private struct PendingFrame
        {
            public Vortice.Direct3D11.ID3D11Texture2D StagingTexture;
            public uint Width;
            public uint Height;
            public long TimestampUs;
        }

        [ComImport]
        [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDirect3DDxgiInterfaceAccess
        {
            void GetInterface([In] ref Guid iid, [Out] out IntPtr ppv);
        }

        [ComImport]
        [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureItemInterop
        {
            IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
            IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
        private const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;

        public ScreenCapturer(VideoSource source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            (_width, _height) = ScreenHelper.GetPrimaryScreenDimensions();
        }

        public void Start()
        {
            if (_session != null)
                throw new InvalidOperationException("Already capturing");

            InitializeCapture();

            // Start dedicated processing thread — high priority, pinned off capture thread
            _processingThread = new Thread(ProcessingLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
                Name = "ScreenCapture-Processor"
            };
            _processingThread.Start();
        }

        private void InitializeCapture()
        {
            EnsureDispatcherQueue();

            _device = CreateD3DDevice(out _d3dDevice);

            // Pre-allocate staging texture pool
            _stagingPool = new StagingEntry[StagingPoolSize];
            for (int i = 0; i < StagingPoolSize; i++)
            {
                _stagingPool[i] = CreateStagingTexture(_width, _height);
            }

            var monitor = MonitorFromWindow(nint.Zero, MONITOR_DEFAULTTOPRIMARY);
            _item = CreateItemForMonitor(monitor);

            // Increase frame pool buffer count to reduce stalls
            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                bufferCount: 4,   // was 2 — more buffers = fewer dropped frames
                _item.Size);

            _framePool.FrameArrived += OnFrameArrived;

            _session = _framePool.CreateCaptureSession(_item);
            _session.IsBorderRequired = false; // Remove yellow capture border if desired
            _session.StartCapture();

            Console.WriteLine($"Started screen capture: {_item.DisplayName}");
        }

        private StagingEntry CreateStagingTexture(uint width, uint height)
        {
            var desc = new Vortice.Direct3D11.Texture2DDescription
            {
                Width = (int)width,
                Height = (int)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Vortice.DXGI.Format.B8G8R8A8_UNorm,
                SampleDescription = new Vortice.DXGI.SampleDescription(1, 0),
                Usage = Vortice.Direct3D11.ResourceUsage.Staging,
                BindFlags = Vortice.Direct3D11.BindFlags.None,
                CpuAccessFlags = Vortice.Direct3D11.CpuAccessFlags.Read,
                MiscFlags = Vortice.Direct3D11.ResourceOptionFlags.None
            };
            return new StagingEntry
            {
                Texture = _d3dDevice!.CreateTexture2D(desc),
                Width = width,
                Height = height
            };
        }

        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            // FPS throttle — drop frames we don't need on the capture thread itself
            long now = Stopwatch.GetTimestamp();
            if (now - _lastFrameTick < _frameIntervalTicks)
            {
                using var _ = sender.TryGetNextFrame(); // Must consume to free buffer
                return;
            }
            _lastFrameTick = now;

            try
            {
                using var frame = sender.TryGetNextFrame();
                if (frame == null || _disposed) return;

                var dxgiAccess = frame.Surface.As<IDirect3DDxgiInterfaceAccess>();
                var iid = typeof(Vortice.Direct3D11.ID3D11Texture2D).GUID;
                dxgiAccess.GetInterface(ref iid, out nint texturePtr);

                var frameTexture = new Vortice.Direct3D11.ID3D11Texture2D(texturePtr);
                var desc = frameTexture.Description;

                // Pick next staging texture from pool (round-robin)
                // CopyResource is async on GPU — the actual stall happens at Map() in the worker
                int poolIdx = Interlocked.Increment(ref _stagingIndex) % StagingPoolSize;
                var staging = _stagingPool![poolIdx];

                // Recreate pool entry only if resolution changed (rare)
                if (staging.Width != desc.Width || staging.Height != desc.Height)
                {
                    staging.Texture.Dispose();
                    _stagingPool[poolIdx] = staging = CreateStagingTexture((uint)desc.Width, (uint)desc.Height);
                }

                lock (_d3dLock)
                {
                    _d3dContext!.CopyResource(staging.Texture, frameTexture);
                }
                frameTexture.Dispose();

                long timestampUs = Stopwatch.GetTimestamp() * 1_000_000 / Stopwatch.Frequency;

                // Non-blocking add — drop frame if queue is full (sender is too slow)
                var pending = new PendingFrame
                {
                    StagingTexture = staging.Texture,
                    Width = (uint)desc.Width,
                    Height = (uint)desc.Height,
                    TimestampUs = timestampUs
                };

                if (!_frameQueue.TryAdd(pending, millisecondsTimeout: 0))
                {
                    // Queue full — skip this frame, prevents unbounded latency buildup
                    Console.WriteLine("[ScreenCapturer] Frame dropped (queue full)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OnFrameArrived] {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Dedicated thread: GPU→CPU readback + send to LiveKit
        private void ProcessingLoop()
        {
            foreach (var pending in _frameQueue.GetConsumingEnumerable())
            {
                if (_disposed) break;
                try
                {
                    Vortice.Direct3D11.MappedSubresource mapped;
                    lock (_d3dLock)
                    {
                        mapped = _d3dContext!.Map(
                            pending.StagingTexture, 0,
                            Vortice.Direct3D11.MapMode.Read,
                            Vortice.Direct3D11.MapFlags.None);
                    }

                    try
                    {
                        var bufferInfo = new VideoBufferInfo
                        {
                            Type = VideoBufferType.Bgra,
                            Width = pending.Width,
                            Height = pending.Height,
                            DataPtr = (ulong)mapped.DataPointer,
                            Stride = (uint)mapped.RowPitch
                        };
                        _source.CaptureFrame(bufferInfo, pending.TimestampUs);
                    }
                    finally
                    {
                        lock (_d3dLock)
                        {
                            _d3dContext!.Unmap(pending.StagingTexture, 0);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ProcessingLoop] {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        // ── D3D / WinRT plumbing (unchanged from original) ──────────────────

        private IDirect3DDevice CreateD3DDevice(out Vortice.Direct3D11.ID3D11Device d3dDevice)
        {
            Vortice.Direct3D11.D3D11.D3D11CreateDevice(
                null,
                Vortice.Direct3D.DriverType.Hardware,
                Vortice.Direct3D11.DeviceCreationFlags.BgraSupport,
                null,
                out d3dDevice,
                out _d3dContext);

            using var dxgiDevice = d3dDevice.QueryInterface<Vortice.DXGI.IDXGIDevice>();
            return CreateDirect3DDeviceFromDXGIDevice(dxgiDevice.NativePointer);
        }

        [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice",
            SetLastError = true, ExactSpelling = true,
            CallingConvention = CallingConvention.StdCall)]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(
            nint dxgiDevice, out nint graphicsDevice);

        private static IDirect3DDevice CreateDirect3DDeviceFromDXGIDevice(nint dxgiDevice)
        {
            int hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out nint pUnknown);
            if (hr != 0)
                throw new Exception($"CreateDirect3D11DeviceFromDXGIDevice failed: 0x{hr:X8}");
            try
            {
                return MarshalInterface<IDirect3DDevice>.FromAbi(pUnknown);
            }
            finally
            {
                if (pUnknown != nint.Zero) Marshal.Release(pUnknown);
            }
        }

        [DllImport("api-ms-win-core-winrt-l1-1-0.dll")]
        private static extern int RoGetActivationFactory(
            nint activatableClassId, ref Guid iid, out nint factory);

        [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll",
            CallingConvention = CallingConvention.StdCall)]
        private static extern int WindowsCreateString(
            [MarshalAs(UnmanagedType.LPWStr)] string src, uint len, out nint hstr);

        [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll",
            CallingConvention = CallingConvention.StdCall)]
        private static extern int WindowsDeleteString(nint hstr);

        private GraphicsCaptureItem CreateItemForMonitor(nint hmon)
        {
            var iidUnknown = Guid.Parse("00000000-0000-0000-C000-000000000046");
            var iidCaptureItem = Guid.Parse("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            nint hstring = nint.Zero, itemPtr = nint.Zero, factoryPtr = nint.Zero;
            try
            {
                var id = "Windows.Graphics.Capture.GraphicsCaptureItem";
                if (WindowsCreateString(id, (uint)id.Length, out hstring) != 0)
                    throw new Exception("WindowsCreateString failed");
                if (RoGetActivationFactory(hstring, ref iidUnknown, out factoryPtr) != 0)
                    throw new Exception("RoGetActivationFactory failed");
                var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);
                itemPtr = interop.CreateForMonitor(hmon, ref iidCaptureItem);
                return MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr);
            }
            finally
            {
                if (hstring != nint.Zero) WindowsDeleteString(hstring);
                if (itemPtr != nint.Zero) Marshal.Release(itemPtr);
                if (factoryPtr != nint.Zero) Marshal.Release(factoryPtr);
            }
        }

        private void EnsureDispatcherQueue()
        {
            if (TryGetDispatcherQueue() != nint.Zero) return;
            var options = new DispatcherQueueOptions
            {
                dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
                threadType = DispatcherQueueThreadType.DQTYPE_THREAD_CURRENT,
                apartmentType = DispatcherQueueThreadApartmentType.DQTAT_COM_NONE
            };
            int hr = CreateDispatcherQueueController(options, out var ctrl);
            if (hr != 0)
                throw new Exception($"CreateDispatcherQueueController failed: 0x{hr:X8}");
            _dispatcherQueueController = ctrl;
        }

        [DllImport("CoreMessaging.dll")]
        private static extern int CreateDispatcherQueueController(
            [In] DispatcherQueueOptions options, [Out] out nint dispatcherQueueController);

        [StructLayout(LayoutKind.Sequential)]
        private struct DispatcherQueueOptions
        {
            public int dwSize;
            public DispatcherQueueThreadType threadType;
            public DispatcherQueueThreadApartmentType apartmentType;
        }

        private enum DispatcherQueueThreadType { DQTYPE_THREAD_DEDICATED = 1, DQTYPE_THREAD_CURRENT = 2 }
        private enum DispatcherQueueThreadApartmentType { DQTAT_COM_NONE = 0, DQTAT_COM_ASTA = 1, DQTAT_COM_STA = 2 }

        private nint TryGetDispatcherQueue()
        {
            try { return Windows.System.DispatcherQueue.GetForCurrentThread() != null ? 1 : nint.Zero; }
            catch { return nint.Zero; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _frameQueue.CompleteAdding();
            _processingThread?.Join(timeout: TimeSpan.FromSeconds(2));

            _session?.Dispose();
            _framePool?.Dispose();

            if (_stagingPool != null)
                foreach (var entry in _stagingPool)
                    entry.Texture?.Dispose();

            _d3dContext?.Dispose();
            _d3dDevice?.Dispose();
            _device = null;

            if (_dispatcherQueueController is nint ptr && ptr != nint.Zero)
                Marshal.Release(ptr);
        }
    }
}
