using LiveKit.Proto;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using WinRT;

namespace LiveKit.Client
{
    /// <summary>
    /// Captures screen content using Windows.Graphics.Capture and pushes frames to a video source.
    /// </summary>
    public class ScreenCapturer : IDisposable
    {
        private readonly VideoSource _source;
        private readonly uint _width;
        private readonly uint _height;
        private GraphicsCaptureItem? _item;
        private Direct3D11CaptureFramePool? _framePool;
        private GraphicsCaptureSession? _session;
        private Vortice.Direct3D11.ID3D11Device? _d3dDevice;
        private Vortice.Direct3D11.ID3D11DeviceContext? _d3dContext;
        private Vortice.Direct3D11.ID3D11Texture2D? _stagingTexture;
        private uint _stagingTextureWidth;
        private uint _stagingTextureHeight;
        private IDirect3DDevice? _device;
        private object? _dispatcherQueueController; // Using object to avoid referencing WinRT types directly if not needed, but we need the pointer
        private bool _disposed;

        [ComImport]
        [Guid("5B0D3235-4DBA-4d44-865E-8F1D0E4FD04D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        unsafe interface IMemoryBufferByteAccess
        {
            void GetBuffer(out byte* buffer, out uint capacity);
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

            // Use dynamic screen dimensions
            var (dynamicWidth, dynamicHeight) = ScreenHelper.GetPrimaryScreenDimensions();
            _width = dynamicWidth;
            _height = dynamicHeight;
        }

        public void Start()
        {
            if (_session != null)
                throw new InvalidOperationException("Already capturing");

            InitializeCapture();
        }

        private void InitializeCapture()
        {
            // 0. Ensure DispatcherQueue exists (needed for Console Apps)
            EnsureDispatcherQueue();

            // 1. Initialize D3D11 Device
            _device = CreateD3DDevice(out _d3dDevice);

            // 2. Create Capture Item (Primary Monitor)
            var monitor = MonitorFromWindow(nint.Zero, MONITOR_DEFAULTTOPRIMARY);
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
            try
            {
                using var frame = sender.TryGetNextFrame();
                if (frame == null) return;
                ProcessFrame(frame);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing frame: {ex.Message}");
            }
        }

        private void ProcessFrame(Direct3D11CaptureFrame frame)
        {
            try
            {
                var dxgiInterfaceAccess = frame.Surface.As<IDirect3DDxgiInterfaceAccess>();
                var iidTexture2D = typeof(Vortice.Direct3D11.ID3D11Texture2D).GUID;
                dxgiInterfaceAccess.GetInterface(ref iidTexture2D, out nint texturePtr);
                
                var frameTexture = new Vortice.Direct3D11.ID3D11Texture2D(texturePtr);
                try
                {
                    var desc = frameTexture.Description;
                    
                    if (_stagingTexture == null || _stagingTextureWidth != desc.Width || _stagingTextureHeight != desc.Height)
                    {
                        if (_stagingTexture != null)
                        {
                            _stagingTexture.Dispose();
                            _stagingTexture = null;
                        }
                        
                        var stagingDesc = new Vortice.Direct3D11.Texture2DDescription
                        {
                            Width = desc.Width,
                            Height = desc.Height,
                            MipLevels = 1,
                            ArraySize = 1,
                            Format = desc.Format,
                            SampleDescription = new Vortice.DXGI.SampleDescription(1, 0),
                            Usage = Vortice.Direct3D11.ResourceUsage.Staging,
                            BindFlags = Vortice.Direct3D11.BindFlags.None,
                            CpuAccessFlags = Vortice.Direct3D11.CpuAccessFlags.Read,
                            MiscFlags = Vortice.Direct3D11.ResourceOptionFlags.None
                        };
                        
                        _stagingTexture = _d3dDevice!.CreateTexture2D(stagingDesc);
                        _stagingTextureWidth = desc.Width;
                        _stagingTextureHeight = desc.Height;
                    }
                    
                    _d3dContext!.CopyResource(_stagingTexture, frameTexture);
                    
                    var mappedResource = _d3dContext.Map(_stagingTexture, 0, Vortice.Direct3D11.MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                    try
                    {
                        var bufferInfo = new VideoBufferInfo
                        {
                            Type = VideoBufferType.Bgra,
                            Width = desc.Width,
                            Height = desc.Height,
                            DataPtr = (ulong)mappedResource.DataPointer,
                            Stride = (uint)mappedResource.RowPitch
                        };
                        
                        var timestampUs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;
                        if (_disposed) return;
                        
                        _source.CaptureFrame(bufferInfo, timestampUs);
                    }
                    finally
                    {
                        _d3dContext.Unmap(_stagingTexture, 0);
                    }
                }
                finally
                {
                    frameTexture.Dispose();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ProcessFrame] EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine($"[ProcessFrame] Stack trace: {ex.StackTrace}");
            }
        }

        private IDirect3DDevice CreateD3DDevice(out Vortice.Direct3D11.ID3D11Device d3dDevice)
        {
            // Create D3D11 Device
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

        [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

        private static IDirect3DDevice CreateDirect3DDeviceFromDXGIDevice(nint dxgiDevice)
        {
            nint pUnknown = nint.Zero;
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
                if (pUnknown != nint.Zero) Marshal.Release(pUnknown);
            }
        }

        [DllImport("api-ms-win-core-winrt-l1-1-0.dll")]
        private static extern int RoGetActivationFactory(nint activatableClassId, ref Guid iid, out nint factory);
        [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, uint length, out nint hstring);
        [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int WindowsDeleteString(nint hstring);
        private GraphicsCaptureItem CreateItemForMonitor(nint hmon)
        {
            var iidUnknown = Guid.Parse("00000000-0000-0000-C000-000000000046");
            var iGraphicsCaptureItemIID = Guid.Parse("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            nint hstring = nint.Zero;
            nint itemPtr = nint.Zero;
            nint factoryPtr = nint.Zero;
            try
            {
                var activatableId = "Windows.Graphics.Capture.GraphicsCaptureItem";
                int hr = WindowsCreateString(activatableId, (uint)activatableId.Length, out hstring);
                if (hr != 0) throw new Exception("Failed to create string");
                hr = RoGetActivationFactory(hstring, ref iidUnknown, out factoryPtr);
                if (hr != 0) throw new Exception("Failed to get activation factory");
                var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);
                itemPtr = interop.CreateForMonitor(hmon, ref iGraphicsCaptureItemIID);
                var captureItem = MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr);
                return captureItem;

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
            // Check if we already have a DispatcherQueue
            if (TryGetDispatcherQueue() != nint.Zero)
            {
                return;
            }


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
            [Out] out nint dispatcherQueueController);

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


        private nint TryGetDispatcherQueue()
        {
            try
            {

                var queue = Windows.System.DispatcherQueue.GetForCurrentThread();
                return queue != null ? 1 : nint.Zero; // Just return non-zero if exists
            }
            catch
            {
                return nint.Zero;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _session?.Dispose();
            _framePool?.Dispose();

            if (_stagingTexture != null)
            {
                _stagingTexture.Dispose();
                _stagingTexture = null;
            }

            if (_d3dContext != null)
            {
                _d3dContext.Dispose();
                _d3dContext = null;
            }

            if (_d3dDevice != null)
            {
                _d3dDevice.Dispose();
                _d3dDevice = null;
            }
            if (_device != null)
            {

                _device = null;
            }
            if (_dispatcherQueueController is nint controllerPtr && controllerPtr != nint.Zero)
            {
                Marshal.Release(controllerPtr);
                _dispatcherQueueController = null;
            }

            _disposed = true;
        }
    }
}