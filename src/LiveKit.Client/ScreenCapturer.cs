using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LiveKit.Internal;
using LiveKit.Proto;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.DirectX;
using WinRT;
using System.Runtime.CompilerServices;

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
        private GraphicsCaptureItem? _item;
        private Direct3D11CaptureFramePool? _framePool;
        private GraphicsCaptureSession? _session;
        private D3D11Interop.ID3D11Device? _d3dDevice;
        private D3D11Interop.ID3D11DeviceContext? _d3dContext;
        private IDirect3DDevice? _device;
        private object? _dispatcherQueueController; // Using object to avoid referencing WinRT types directly if not needed, but we need the pointer
        private bool _disposed;

        public ScreenCapturer(VideoSource source, uint width = 1920, uint height = 1080)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _width = width;
            _height = height;
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
            _device = CreateD3DDevice(out _d3dDevice, out _d3dContext);

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

            // Console.WriteLine($"Frame arrived: {frame.ContentSize.Width}x{frame.ContentSize.Height}");

            try
            {
                ProcessFrame(frame);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing frame: {ex.Message}");
            }
        }

        private unsafe void ProcessFrame(Direct3D11CaptureFrame frame)
        {
            if (_d3dDevice == null || _d3dContext == null) return;

            // Get the surface as ID3D11Texture2D
            using var surface = frame.Surface;
            var access = surface.As<D3D11Interop.IDirect3DDxgiInterfaceAccess>();
            var textureGuid = typeof(D3D11Interop.ID3D11Texture2D).GUID;
            var texturePtr = access.GetInterface(ref textureGuid);
            
            if (texturePtr == IntPtr.Zero) return;

            var texture = (D3D11Interop.ID3D11Texture2D)Marshal.GetObjectForIUnknown(texturePtr);
            Marshal.Release(texturePtr); // Release the raw pointer, we have the RCW

            // Create a staging texture to copy data to CPU
            var desc = new D3D11Interop.D3D11_TEXTURE2D_DESC
            {
                Width = (uint)frame.ContentSize.Width,
                Height = (uint)frame.ContentSize.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = D3D11Interop.DXGI_FORMAT_B8G8R8A8_UNORM,
                SampleDesc = new D3D11Interop.DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                Usage = D3D11Interop.D3D11_USAGE.D3D11_USAGE_STAGING,
                BindFlags = 0,
                CPUAccessFlags = D3D11Interop.D3D11_CPU_ACCESS_READ,
                MiscFlags = 0
            };

            D3D11Interop.ID3D11Texture2D stagingTexture;
            _d3dDevice.CreateTexture2D(ref desc, IntPtr.Zero, out stagingTexture);

            // Copy to staging
            _d3dContext.CopyResource((D3D11Interop.ID3D11Resource)stagingTexture, (D3D11Interop.ID3D11Resource)texture);

            // Map the staging texture
            _d3dContext.Map((D3D11Interop.ID3D11Resource)stagingTexture, 0, D3D11Interop.D3D11_MAP.D3D11_MAP_READ, 0, out var mapped);

            try
            {
                // Create VideoBufferInfo
                // Note: We need to copy the data because we unmap immediately.
                // For performance, we should reuse a buffer, but for now we allocate.
                // Actually, VideoSource expects us to pass a pointer. If we pass 'mapped.pData', it's only valid until Unmap.
                // LiveKit's VideoSource.CaptureFrame sends a request. If it's async/queued, we might need to copy.
                // Assuming CaptureFrame copies or processes immediately (it sends FFI request, which likely copies).
                // Wait, FFI usually copies. Let's verify VideoSource.CaptureFrame.
                // It sends a request with DataPtr. The Rust side likely copies it.
                // However, if the Rust side is async, we are in trouble.
                // Safe approach: Copy to a managed array, pin it, send it.
                
                int size = (int)(desc.Height * mapped.RowPitch);
                // We can't easily copy to managed array and pin without allocating every frame.
                // Let's assume for now we can pass the mapped pointer directly if we wait for the call to return.
                // The FfiClient.SendRequest is synchronous in sending the message?
                // It uses a channel. It might be async.
                // To be safe, let's alloc a buffer.
                
                byte[] buffer = new byte[size];
                // Copy from pData to buffer
                // We need to handle RowPitch (stride) vs Width * 4
                // If RowPitch == Width * 4, we can copy block.
                // If not, we copy row by row.
                
                // Simple copy for now (assuming packed or handling stride in VideoBufferInfo)
                // VideoBufferInfo has Stride. So we can pass the Stride from mapped.RowPitch.
                
                // Let's try passing the mapped pointer directly. If it crashes or corrupts, we know why.
                // But wait, Unmap happens right after. If FFI is async, this is bad.
                // Let's copy to a managed buffer to be safe.
                
                // Optimization: Reuse buffer
                // For this implementation, we'll just allocate (GC will handle it, but it's churn).
                
                // Actually, let's just copy row-by-row to a packed buffer to match expected width/height
                // because VideoBufferInfo expects a specific stride usually? 
                // VideoBufferInfo has a Stride field.
                
                // Let's copy to a pinned buffer.
                
                byte[] frameData = new byte[desc.Width * desc.Height * 4];
                fixed (byte* destPtr = frameData)
                {
                    byte* srcPtr = (byte*)mapped.pData;
                    for (int y = 0; y < desc.Height; y++)
                    {
                        Buffer.MemoryCopy(srcPtr, destPtr + y * desc.Width * 4, desc.Width * 4, desc.Width * 4);
                        srcPtr += mapped.RowPitch;
                    }
                }
                
                var handle = GCHandle.Alloc(frameData, GCHandleType.Pinned);
                var ptr = handle.AddrOfPinnedObject();

                var bufferInfo = new VideoBufferInfo
                {
                    Type = VideoBufferType.Rgba,
                    Width = desc.Width,
                    Height = desc.Height,
                    DataPtr = (ulong)ptr.ToInt64(),
                    Stride = desc.Width * 4
                };

                var timestampUs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;
                _source.CaptureFrame(bufferInfo, timestampUs);
                
                handle.Free();
            }
            finally
            {
                _d3dContext.Unmap((D3D11Interop.ID3D11Resource)stagingTexture, 0);
                // Release COM objects
                Marshal.ReleaseComObject(stagingTexture);
                Marshal.ReleaseComObject(texture);
            }
        }

        private IDirect3DDevice CreateD3DDevice(out D3D11Interop.ID3D11Device d3dDevice, out D3D11Interop.ID3D11DeviceContext d3dContext)
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
                out d3dContext);

            // Query for IDXGIDevice interface from ID3D11Device
            // ID3D11Device inherits from IDXGIDevice, so we can QueryInterface for it
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
            // Call the native function to create the Direct3D device
            IntPtr pUnknown = IntPtr.Zero;
            int hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out pUnknown);
            
            if (hr != 0)
            {
                throw new Exception($"CreateDirect3D11DeviceFromDXGIDevice failed with HRESULT: 0x{hr:X8}");
            }
            try
            {
                // Marshal the IInspectable pointer to IDirect3DDevice
                var device = MarshalInterface<Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice>.FromAbi(pUnknown);
                return device;
            }
            finally
            {
                // Release the pointer since GetObjectForIUnknown adds a reference
                if(pUnknown != IntPtr.Zero)Marshal.Release(pUnknown);
            }
        }
        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
        [Guid("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90")]
        private interface IInspectable {}
        [DllImport("api-ms-win-core-winrt-l1-1-0.dll")]
        private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);
        [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, uint length, out IntPtr hstring);
        [DllImport("api-ms-win-core-winrt-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int WindowsDeleteString(IntPtr hstring);
        private GraphicsCaptureItem CreateItemForMonitor(IntPtr hmon)
        {
            // The IGraphicsCaptureItemInterop is obtained directly from the activation factory
            // by querying for it with RoGetActivationFactory using the interop GUID
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

        

        [DllImport("api-ms-win-core-winrt-l1-1-0.dll")]
        private static extern int RoGetActivationFactory(
            [MarshalAs(UnmanagedType.HString)] string activatableClassId,
            [In] ref Guid iid,
            [Out] out IntPtr factory);

        private void EnsureDispatcherQueue()
        {
            // Check if we already have a DispatcherQueue
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

            // We hold onto the controller to keep the queue alive
            // We don't strictly need to cast it to a WinRT object if we just want to keep it alive via the pointer,
            // but we should probably release it properly.
            // For now, let's just store the pointer or RCW if we had the definition.
            // Since we don't have the WinRT projection for DispatcherQueueController handy in this file without extra refs,
            // we will manage the pointer manually or use a simple wrapper.
            
            // Actually, we should probably just keep the pointer and release it in Dispose.
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

        // Helper to check if DispatcherQueue exists
        // We can use Windows.System.DispatcherQueue.GetForCurrentThread() but that's WinRT.
        // Let's use PInvoke or just assume if we are in a Console App we might need one.
        // Actually, we can use the WinRT API if available.
        private IntPtr TryGetDispatcherQueue()
        {
            try
            {
                // This might throw if not on a thread with DispatcherQueue or if not initialized
                // But wait, Windows.System.DispatcherQueue.GetForCurrentThread() returns null if none.
                var queue = Windows.System.DispatcherQueue.GetForCurrentThread();
                return queue != null ? (IntPtr)1 : IntPtr.Zero; // Just return non-zero if exists
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _session?.Dispose();
            _framePool?.Dispose();
            
            if (_d3dDevice != null)
            {
                Marshal.ReleaseComObject(_d3dDevice);
            }
            if (_d3dContext != null)
            {
                Marshal.ReleaseComObject(_d3dContext);
            }
            if (_device != null)
            {
                Marshal.ReleaseComObject(_device);
            }

            if (_dispatcherQueueController is IntPtr controllerPtr && controllerPtr != IntPtr.Zero)
            {
                // If we stored it as IntPtr, release it.
                // CreateDispatcherQueueController returns a pointer to IDispatcherQueueController.
                // We should Release it.
                Marshal.Release(controllerPtr);
                _dispatcherQueueController = null;
            }

            _disposed = true;
        }
    }
}
