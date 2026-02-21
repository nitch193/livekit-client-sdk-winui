using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LiveKit.Internal;
using LiveKit.Proto;
// Windows-specific imports commented out for cross-platform test pattern generation
// using Windows.Graphics.Capture;
// using Windows.Graphics.DirectX.Direct3D11;
// using Windows.Graphics.DirectX;
// using Windows.Graphics.Imaging;
// using WinRT;
// using System.Runtime.CompilerServices;

namespace LiveKit
{
    /// <summary>
    /// Captures screen content using Windows.Graphics.Capture and pushes frames to a video source.
    /// </summary>
    public class ScreenCapturer : IDisposable
    {
        private readonly VideoSource _source;
        private readonly uint _width;
        private readonly uint _height;
        // Windows-specific fields commented out
        // private GraphicsCaptureItem? _item;
        // private Direct3D11CaptureFramePool? _framePool;
        // private GraphicsCaptureSession? _session;
        // private D3D11Interop.ID3D11Device? _d3dDevice;
        // private IDirect3DDevice? _device;
        // private object? _dispatcherQueueController;
        private bool _disposed;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _captureTask;

        [ComImport]
        [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        unsafe interface IMemoryBufferByteAccess
        {
            void GetBuffer(out byte* buffer, out uint capacity);
        }

        public ScreenCapturer(VideoSource source, uint width = 1920, uint height = 1080)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _width = width;
            _height = height;
        }

        public void Start()
        {
            if (_captureTask != null)
                throw new InvalidOperationException("Already capturing");

            _cancellationTokenSource = new CancellationTokenSource();
            _captureTask = Task.Run(() => GenerateTestPattern(_cancellationTokenSource.Token));
            Console.WriteLine("Started test pattern generation");
        }

        private async Task GenerateTestPattern(CancellationToken cancellationToken)
        {
            const int fps = 30;
            const int frameDelayMs = 1000 / fps;
            
            // Allocate buffer for RGBA frame
            int bufferSize = (int)(_width * _height * 4); // 4 bytes per pixel (RGBA)
            byte[] frameBuffer = new byte[bufferSize];
            
            int frameCount = 0;
            
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Generate color cycling test pattern
                    GenerateColorCyclePattern(frameBuffer, frameCount);
                    
                    unsafe
                    {
                        fixed (byte* ptr = frameBuffer)
                        {
                            var bufferInfo = new VideoBufferInfo
                            {
                                Type = VideoBufferType.Rgba,
                                Width = _width,
                                Height = _height,
                                DataPtr = (ulong)ptr,
                                Stride = _width * 4
                            };

                            var timestampUs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;
                            _source.CaptureFrame(bufferInfo, timestampUs);
                        }
                    }
                    
                    frameCount++;
                    await Task.Delay(frameDelayMs, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error generating test pattern: {ex.Message}");
                }
            }
        }

        private void GenerateColorCyclePattern(byte[] buffer, int frameCount)
        {
            // Create a color cycling pattern that changes over time
            float hue = (frameCount % 360) / 360.0f;
            
            for (uint y = 0; y < _height; y++)
            {
                for (uint x = 0; x < _width; x++)
                {
                    int index = (int)((y * _width + x) * 4);
                    
                    // Create gradient based on position and time
                    float xRatio = x / (float)_width;
                    float yRatio = y / (float)_height;
                    
                    // HSV to RGB conversion for smooth color cycling
                    float h = (hue + xRatio * 0.3f + yRatio * 0.3f) % 1.0f;
                    float s = 0.8f;
                    float v = 0.9f;
                    
                    (byte r, byte g, byte b) = HsvToRgb(h, s, v);
                    
                    buffer[index] = r;     // R
                    buffer[index + 1] = g; // G
                    buffer[index + 2] = b; // B
                    buffer[index + 3] = 255; // A
                }
            }
        }

        private static (byte r, byte g, byte b) HsvToRgb(float h, float s, float v)
        {
            int hi = (int)(h * 6) % 6;
            float f = h * 6 - hi;
            float p = v * (1 - s);
            float q = v * (1 - f * s);
            float t = v * (1 - (1 - f) * s);

            float r, g, b;
            switch (hi)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }

            return ((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        /* Windows-specific capture code commented out
        private void InitializeCapture()
        {
            // 0. Ensure DispatcherQueue exists
            EnsureDispatcherQueue();

            // 1. Initialize D3D11 Device
            _device = CreateD3DDevice(out _d3dDevice);

            // 2. Create Capture Item (Primary Monitor)
            var monitor = D3D11Interop.MonitorFromWindow(IntPtr.Zero, D3D11Interop.MONITOR_DEFAULTTOPRIMARY);
            _item = CreateItemForMonitor(monitor);

            // 3. Create Frame Pool
            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                _item.Size);

            _framePool.FrameArrived += OnFrameArrived;

            // 4. Create Session
            _session = _framePool.CreateCaptureSession(_item);
            _session.StartCapture();
            
            Console.WriteLine($"Started screen capture for item: {_item.DisplayName}");
        }


        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            using var frame = sender.TryGetNextFrame();
            if (frame == null) return;

            try
            {
                // We need to run async code here, but FrameArrived is void-returning.
                // We'll fire and forget, but handle exceptions.
                _ = ProcessFrameAsync(frame);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing frame: {ex.Message}");
            }
        }

        private async Task ProcessFrameAsync(Direct3D11CaptureFrame frame)
        {
            try
            {
                // Convert the surface to a SoftwareBitmap
                var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
                
                using (var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read))
                using (var reference = buffer.CreateReference())
                {
                    unsafe
                    {
                        var byteAccess = reference.As<IMemoryBufferByteAccess>();
                        byteAccess.GetBuffer(out byte* dataPtr, out uint capacity);

                        var plane = buffer.GetPlaneDescription(0);
                        int stride = plane.Stride;
                        int width = bitmap.PixelWidth;
                        int height = bitmap.PixelHeight;

                        // Direct pointer access optimization
                        // We assume VideoSource.CaptureFrame (and FFI) consumes data synchronously or copies it.
                        // Based on FfiClient.SendRequest, it seems to serialize/copy before returning.
                        
                        var bufferInfo = new VideoBufferInfo
                        {
                            Type = VideoBufferType.Rgba,
                            Width = (uint)width,
                            Height = (uint)height,
                            DataPtr = (ulong)dataPtr,
                            Stride = (uint)stride
                        };

                        var timestampUs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;
                        _source.CaptureFrame(bufferInfo, timestampUs);
                    }
                }
                
                bitmap.Dispose();
            }
            catch (Exception ex)
            {
                // Console.WriteLine($"[ProcessFrame] EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            }
        }
        */

        /* All Windows-specific interop methods commented out
        private IDirect3DDevice CreateD3DDevice(out D3D11Interop.ID3D11Device d3dDevice)
        {
            // Create D3D11 Device
            D3D11Interop.D3D11CreateDevice(
                IntPtr.Zero,
                D3D11Interop.D3D_DRIVER_TYPE_HARDWARE,
                IntPtr.Zero,
                D3D11Interop.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                IntPtr.Zero,
                0,
                D3D11Interop.D3D11_SDK_VERSION,
                out d3dDevice,
                out int featureLevel,
                out var d3dContext); // We don't need context anymore for manual copy

            // Release context immediately as we don't use it
            if (d3dContext != null) Marshal.ReleaseComObject(d3dContext);

            // Query for IDXGIDevice interface from ID3D11Device
            var idxgiDeviceGuid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c"); // IDXGIDevice
            IntPtr pUnknown = Marshal.GetIUnknownForObject(d3dDevice);
            IntPtr idxgiDevicePtr = IntPtr.Zero;
            try {
                int hr = Marshal.QueryInterface(pUnknown, ref idxgiDeviceGuid, out idxgiDevicePtr);
                if (hr != 0) {
                    throw new Exception("Failed to query IDXGIDevice interface from ID3D11Device");
                }
                var device = CreateDirect3DDeviceFromDXGIDevice(idxgiDevicePtr);
                return device;

            }finally {
                if(idxgiDevicePtr != IntPtr.Zero)Marshal.Release(idxgiDevicePtr);
                if(pUnknown != IntPtr.Zero)Marshal.Release(pUnknown);
            }
        }

        [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

        private static IDirect3DDevice CreateDirect3DDeviceFromDXGIDevice(IntPtr dxgiDevice)
        {
            IntPtr pUnknown = IntPtr.Zero;
            int hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out pUnknown);
            
            if (hr != 0)
            {
                throw new Exception($"CreateDirect3D11DeviceFromDXGIDevice failed with HRESULT: 0x{hr:X8}");
            }
            try
            {
                var device = MarshalInterface<Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice>.FromAbi(pUnknown);
                return device;
            }
            finally
            {
                if(pUnknown != IntPtr.Zero)Marshal.Release(pUnknown);
            }
        }

        [DllImport("api-ms-win-core-winrt-l1-1-0.dll")]
        private static extern int RoGetActivationFactory(
            IntPtr activatableClassId,
            [In] ref Guid iid,
            [Out] out IntPtr factory);

        [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, uint length, out IntPtr hstring);
        
        [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int WindowsDeleteString(IntPtr hstring);

        private GraphicsCaptureItem CreateItemForMonitor(IntPtr hmon)
        {
            var iidUnknown = Guid.Parse("00000000-0000-0000-C000-000000000046");
            var iGraphicsCaptureItemIID = Guid.Parse("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            IntPtr hstring = IntPtr.Zero;
            IntPtr itemPtr = IntPtr.Zero;
            IntPtr factoryPtr = IntPtr.Zero;
            try {
                var activatableId = "Windows.Graphics.Capture.GraphicsCaptureItem";
                int hr = WindowsCreateString(activatableId, (uint)activatableId.Length, out hstring);
                if (hr != 0) throw new Exception("Failed to create string");
                hr = RoGetActivationFactory(hstring, ref iidUnknown, out factoryPtr);
                if (hr != 0) throw new Exception("Failed to get activation factory");
                var interop = (D3D11Interop.IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);
                itemPtr = interop.CreateForMonitor(hmon, ref iGraphicsCaptureItemIID);
                var captureItem = MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr);
                return captureItem;

            }finally{
                if(hstring != IntPtr.Zero) WindowsDeleteString(hstring);
                if(itemPtr != IntPtr.Zero) Marshal.Release(itemPtr);
                if(factoryPtr != IntPtr.Zero) Marshal.Release(factoryPtr);
            }
        }

        private void EnsureDispatcherQueue()
        {
            if (TryGetDispatcherQueue() != IntPtr.Zero)
            {
                return;
            }

            Console.WriteLine("Creating DispatcherQueueController...");

            var options = new DispatcherQueueOptions
            {
                dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
                threadType = DispatcherQueueThreadType.DQTYPE_THREAD_CURRENT,
                apartmentType = DispatcherQueueThreadApartmentType.DQTAT_COM_NONE
            };

            int hr = CreateDispatcherQueueController(options, out var controllerPtr);
            
            if (hr != 0)
            {
                throw new Exception($"CreateDispatcherQueueController failed with HRESULT: 0x{hr:X8}");
            }

            _dispatcherQueueController = controllerPtr;
        }

        [DllImport("CoreMessaging.dll")]
        private static extern int CreateDispatcherQueueController(
            [In] DispatcherQueueOptions options,
            [Out] out IntPtr dispatcherQueueController);

        [StructLayout(LayoutKind.Sequential)]
        private struct DispatcherQueueOptions
        {
            public int dwSize;
            public DispatcherQueueThreadType threadType;
            public DispatcherQueueThreadApartmentType apartmentType;
        }

        private enum DispatcherQueueThreadType
        {
            DQTYPE_THREAD_DEDICATED = 1,
            DQTYPE_THREAD_CURRENT = 2,
        }

        private enum DispatcherQueueThreadApartmentType
        {
            DQTAT_COM_NONE = 0,
            DQTAT_COM_ASTA = 1,
            DQTAT_COM_STA = 2
        }

        private IntPtr TryGetDispatcherQueue()
        {
            try
            {
                var queue = Windows.System.DispatcherQueue.GetForCurrentThread();
                return queue != null ? (IntPtr)1 : IntPtr.Zero;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }
        */

        public void Dispose()
        {
            if (_disposed) return;

            // Stop test pattern generation
            _cancellationTokenSource?.Cancel();
            _captureTask?.Wait(TimeSpan.FromSeconds(1));
            _cancellationTokenSource?.Dispose();

            /* Windows-specific cleanup commented out
            _session?.Dispose();
            _framePool?.Dispose();
            
            if (_d3dDevice != null)
            {
                Marshal.ReleaseComObject(_d3dDevice);
            }
            if (_device != null)
            {
                Marshal.ReleaseComObject(_device);
            }

            if (_dispatcherQueueController is IntPtr controllerPtr && controllerPtr != IntPtr.Zero)
            {
                Marshal.Release(controllerPtr);
                _dispatcherQueueController = null;
            }
            */

            _disposed = true;
        }
    }
}
