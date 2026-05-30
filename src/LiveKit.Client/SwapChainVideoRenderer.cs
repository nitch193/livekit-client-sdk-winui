using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Windows.Graphics.DirectX;
using LiveKit.Proto;

namespace LiveKit.Client
{
    public class SwapChainVideoRenderer : IDisposable
    {
        private CanvasSwapChainPanel _swapChainPanel;
        private CanvasSwapChain _swapChain;
        private CanvasDevice _canvasDevice;
        private DispatcherQueue _dispatcherQueue;

        public SwapChainVideoRenderer(CanvasSwapChainPanel swapChainPanel)
        {
            _swapChainPanel = swapChainPanel;
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _canvasDevice = CanvasDevice.GetSharedDevice();
        }

        public Task Render(VideoBufferInfo info)
        {
            if (info == null || info.DataPtr == 0) return Task.CompletedTask;

            int width = (int)info.Width;
            int height = (int)info.Height;

            if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
            {
                var tcs = new TaskCompletionSource<bool>();
                _dispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        RenderInternal(info, width, height);
                        tcs.SetResult(true);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                });
                return tcs.Task;
            }
            else
            {
                try
                {
                    RenderInternal(info, width, height);
                }
                catch (Exception ex)
                {
                    return Task.FromException(ex);
                }
                return Task.CompletedTask;
            }
        }

        private void RenderInternal(VideoBufferInfo info, int width, int height)
        {
            EnsureSwapChain(width, height);

            using (var session = _swapChain.CreateDrawingSession(Microsoft.UI.Colors.Transparent))
            {
                // Create a temporary bitmap from the raw pointer
                // Assuming RGBA/BGRA natively from LiveKit. If it's I420, it requires conversion first.
                int byteCount = width * height * 4; 
                byte[] frameData = new byte[byteCount];
                Marshal.Copy((IntPtr)info.DataPtr, frameData, 0, byteCount);

                using (var bitmap = CanvasBitmap.CreateFromBytes(
                    _canvasDevice, 
                    frameData, 
                    width, 
                    height, 
                    DirectXPixelFormat.B8G8R8A8UIntNormalized))
                {
                    session.DrawImage(bitmap);
                }
            }
            _swapChain.Present();
        }

        private void EnsureSwapChain(int width, int height)
        {
            if (_swapChain == null || _swapChain.Size.Width != width || _swapChain.Size.Height != height)
            {
                _swapChain?.Dispose();
                // Create a swapchain matching the video resolution
                _swapChain = new CanvasSwapChain(_canvasDevice, width, height, 96, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, DirectXAlphaMode.Ignore);
                _swapChainPanel.SwapChain = _swapChain;
            }
        }

        public void Dispose()
        {
            _swapChain?.Dispose();
            _canvasDevice?.Dispose();
        }
    }
}
