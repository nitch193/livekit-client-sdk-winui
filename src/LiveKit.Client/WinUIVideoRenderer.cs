using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Microsoft.UI.Xaml.Media.Imaging;
using LiveKit.Proto;

namespace LiveKit
{
    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }

    public class WinUIVideoRenderer
    {
        private SoftwareBitmap? _backBuffer;
        private SoftwareBitmapSource _source;

        public SoftwareBitmapSource Source => _source;

        public WinUIVideoRenderer()
        {
            _source = new SoftwareBitmapSource();
        }

        public async Task RenderFrameAsync(VideoStreamEvent e)
        {
            if (e.FrameReceived == null) return;
            var bufferInfo = e.FrameReceived.Buffer;
            if (bufferInfo == null) return;

            // Basic validation
            if (bufferInfo.Width == 0 || bufferInfo.Height == 0) return;

            // Determine format
            // LiveKit defaults to RGBA (0), but check the type
            var targetFormat = BitmapPixelFormat.Rgba8;
            if (bufferInfo.Type == VideoBufferType.Bgra)
            {
                targetFormat = BitmapPixelFormat.Bgra8;
            }
            // Add more formats if needed, e.g. RGB24, but usually we request RGBA/BGRA upstream

            // Ensure backbuffer exists and matches dimensions/format
            if (_backBuffer == null || 
                _backBuffer.PixelWidth != bufferInfo.Width || 
                _backBuffer.PixelHeight != bufferInfo.Height ||
                _backBuffer.BitmapPixelFormat != targetFormat)
            {
                 // Create new bitmap
                 _backBuffer = new SoftwareBitmap(targetFormat, (int)bufferInfo.Width, (int)bufferInfo.Height, BitmapAlphaMode.Premultiplied);
            }

            // Copy data directly from the pointer
            unsafe
            {
                using (var buffer = _backBuffer.LockBuffer(BitmapBufferAccessMode.Write))
                {
                    using (var reference = buffer.CreateReference())
                    {
                        var byteAccess = reference as IMemoryBufferByteAccess;
                        if (byteAccess != null)
                        {
                            byte* destPtr;
                            uint capacity;
                            byteAccess.GetBuffer(out destPtr, out capacity);

                            // Source pointer from LiveKit
                            byte* srcPtr = (byte*)bufferInfo.DataPtr;
                            
                            // Calculate size to copy
                            // For packed formats (RGBA/BGRA), stride * height is generally safe
                            long sizeToCopy = bufferInfo.Stride * bufferInfo.Height;
                            
                            if (sizeToCopy <= capacity) 
                            {
                                System.Buffer.MemoryCopy(srcPtr, destPtr, capacity, sizeToCopy);
                            }
                        }
                    }
                }
            }
            
            // Update the source
            // Note: This must often be called on the UI thread. The caller is responsible for dispatching if needed.
            await _source.SetBitmapAsync(_backBuffer);
        }
    }
}
