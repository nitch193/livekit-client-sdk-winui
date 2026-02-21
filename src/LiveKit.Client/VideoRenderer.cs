using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime; // For AsStream
using System.Threading.Tasks;
using LiveKit.Proto;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LiveKit.Client
{
    public class VideoRenderer : INotifyPropertyChanged
    {
        private WriteableBitmap _bitmap;
        private readonly DispatcherQueue _dispatcherQueue;

        public event PropertyChangedEventHandler PropertyChanged;

        public VideoRenderer()
        {
            // Capture the DispatcherQueue of the creation thread (expected to be UI thread)
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        }

        public WriteableBitmap Source
        {
            get => _bitmap;
            private set
            {
                if (_bitmap != value)
                {
                    _bitmap = value;
                    OnPropertyChanged();
                }
            }
        }

        public async Task Render(VideoBufferInfo info)
        {
            if (info == null || info.DataPtr == 0) return;

            // Ensure we run on the UI thread
            if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
            {
                var tcs = new TaskCompletionSource<bool>();
                _dispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        RenderInternal(info);
                        tcs.SetResult(true);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                });
                await tcs.Task;
            }
            else
            {
                RenderInternal(info);
            }
        }

        private unsafe void RenderInternal(VideoBufferInfo info)
        {
            int width = (int)info.Width;
            int height = (int)info.Height;

            // Recreate bitmap if dimensions change or it's null
            if (_bitmap == null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
            {
                Source = new WriteableBitmap(width, height);
            }

            // Write pixel data to the bitmap's buffer
            using (var stream = _bitmap.PixelBuffer.AsStream())
            {
                byte* src = (byte*)info.DataPtr;
                int stride = (int)info.Stride;
                
                // If stride matches width * 4 (RGBA/BGRA), we can copy strictly
                // Otherwise we need to copy row by row
                int rowWidth = width * 4;
                
                if (stride == rowWidth)
                {
                   int len = width * height * 4;
                   var span = new Span<byte>(src, len);
                   stream.Seek(0, System.IO.SeekOrigin.Begin);
                   stream.Write(span);
                }
                else
                {
                    stream.Seek(0, System.IO.SeekOrigin.Begin);
                    for (int y = 0; y < height; y++)
                    {
                        var rowSpan = new Span<byte>(src + (y * stride), rowWidth);
                        stream.Write(rowSpan);
                    }
                }
            }

            // Trigger a redraw
            _bitmap.Invalidate();
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
