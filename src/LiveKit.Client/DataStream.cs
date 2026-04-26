using System;
using System.Threading.Tasks;
using LiveKit.Internal;
using LiveKit.Proto;

namespace LiveKit
{
    /// <summary>
    /// High-level data-stream API.  Every async operation gets its own private
    /// <see cref="TaskCompletionSource{T}"/> keyed by the request's AsyncId, so
    /// concurrent calls can never overwrite each other's completion slot (unlike
    /// the old single-field-per-operation pattern).
    ///
    /// The register-before-send contract is enforced in every method:
    ///   1. Stamp AsyncId on the request object.
    ///   2. Register the pending callback slot.
    ///   3. Send the request.
    ///   4. On send failure → cancel the slot so the task doesn't hang.
    /// </summary>
    public class DataStream
    {
        private readonly FfiClient _client;
        private readonly ulong _localParticipantHandle;

        // Push / streaming events (not one-shot, stay as events).
        public event Action<ByteStreamReaderEvent>? ByteStreamReaderEvent;
        public event Action<TextStreamReaderEvent>? TextStreamReaderEvent;

        public DataStream(ulong localParticipantHandle)
        {
            _client = FfiClient.Instance;
            _localParticipantHandle = localParticipantHandle;

            // Subscribe to streaming events (non one-shot).
            _client.ByteStreamReaderEventReceived += OnByteStreamReaderEventReceived;
            _client.TextStreamReaderEventReceived += OnTextStreamReaderEventReceived;
        }

        // ── Byte stream ───────────────────────────────────────────────────────────

        public Task<ulong> OpenByteStreamAsync(StreamByteOptions options)
        {
            var tcs = new TaskCompletionSource<ulong>(TaskCreationOptions.RunContinuationsAsynchronously);

            var req = new ByteStreamOpenRequest
            {
                LocalParticipantHandle = _localParticipantHandle,
                Options = options
            };
            var asyncId = req.InitializeRequestAsyncId();

            _client.RegisterPendingCallback<ByteStreamOpenCallback>(
                asyncId,
                e => e.MessageCase == FfiEvent.MessageOneofCase.ByteStreamOpen ? e.ByteStreamOpen : null,
                cb =>
                {
                    if (cb.Error != null)
                        tcs.TrySetException(new Exception(cb.Error.Description));
                    else
                    {
                        Console.WriteLine($"Byte stream opened: {cb.Writer.Handle.Id}");
                        tcs.TrySetResult(cb.Writer.Handle.Id);
                    }
                },
                onCancel: () => tcs.TrySetCanceled()
            );

            try { _client.SendRequest(new FfiRequest { ByteStreamOpen = req }); }
            catch { _client.CancelPendingCallback(asyncId); throw; }

            return tcs.Task;
        }

        public Task WriteByteStreamAsync(ulong writerHandle, byte[] data)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var req = new ByteStreamWriterWriteRequest
            {
                WriterHandle = writerHandle,
                Bytes = Google.Protobuf.ByteString.CopyFrom(data)
            };
            var asyncId = req.InitializeRequestAsyncId();

            _client.RegisterPendingCallback<ByteStreamWriterWriteCallback>(
                asyncId,
                e => e.MessageCase == FfiEvent.MessageOneofCase.ByteStreamWriterWrite ? e.ByteStreamWriterWrite : null,
                cb =>
                {
                    if (cb.Error != null)
                        tcs.TrySetException(new Exception(cb.Error.Description));
                    else
                    {
                        Console.WriteLine("Byte stream write completed");
                        tcs.TrySetResult(true);
                    }
                },
                onCancel: () => tcs.TrySetCanceled()
            );

            try { _client.SendRequest(new FfiRequest { ByteStreamWrite = req }); }
            catch { _client.CancelPendingCallback(asyncId); throw; }

            return tcs.Task;
        }

        public Task CloseByteStreamAsync(ulong writerHandle)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var req = new ByteStreamWriterCloseRequest { WriterHandle = writerHandle };
            var asyncId = req.InitializeRequestAsyncId();

            _client.RegisterPendingCallback<ByteStreamWriterCloseCallback>(
                asyncId,
                e => e.MessageCase == FfiEvent.MessageOneofCase.ByteStreamWriterClose ? e.ByteStreamWriterClose : null,
                cb =>
                {
                    if (cb.Error != null)
                        tcs.TrySetException(new Exception(cb.Error.Description));
                    else
                    {
                        Console.WriteLine("Byte stream closed");
                        tcs.TrySetResult(true);
                    }
                },
                onCancel: () => tcs.TrySetCanceled()
            );

            try { _client.SendRequest(new FfiRequest { ByteStreamClose = req }); }
            catch { _client.CancelPendingCallback(asyncId); throw; }

            return tcs.Task;
        }

        public Task<byte[]> ReadAllBytesAsync(ulong readerHandle)
        {
            var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

            var req = new ByteStreamReaderReadAllRequest { ReaderHandle = readerHandle };
            var asyncId = req.InitializeRequestAsyncId();

            _client.RegisterPendingCallback<ByteStreamReaderReadAllCallback>(
                asyncId,
                e => e.MessageCase == FfiEvent.MessageOneofCase.ByteStreamReaderReadAll ? e.ByteStreamReaderReadAll : null,
                cb =>
                {
                    if (cb.Error != null)
                        tcs.TrySetException(new Exception(cb.Error.Description));
                    else
                    {
                        Console.WriteLine($"Byte stream read all completed: {cb.Content.Length} bytes");
                        tcs.TrySetResult(cb.Content.ToByteArray());
                    }
                },
                onCancel: () => tcs.TrySetCanceled()
            );

            try { _client.SendRequest(new FfiRequest { ByteReadAll = req }); }
            catch { _client.CancelPendingCallback(asyncId); throw; }

            return tcs.Task;
        }

        public Task<string> WriteBytesToFileAsync(ulong readerHandle, string directory, string nameOverride = "")
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            var req = new ByteStreamReaderWriteToFileRequest
            {
                ReaderHandle = readerHandle,
                Directory = directory,
                NameOverride = nameOverride
            };
            var asyncId = req.InitializeRequestAsyncId();

            _client.RegisterPendingCallback<ByteStreamReaderWriteToFileCallback>(
                asyncId,
                e => e.MessageCase == FfiEvent.MessageOneofCase.ByteStreamReaderWriteToFile ? e.ByteStreamReaderWriteToFile : null,
                cb =>
                {
                    if (cb.Error != null)
                        tcs.TrySetException(new Exception(cb.Error.Description));
                    else
                    {
                        Console.WriteLine($"Byte stream written to file: {cb.FilePath}");
                        tcs.TrySetResult(cb.FilePath);
                    }
                },
                onCancel: () => tcs.TrySetCanceled()
            );

            try { _client.SendRequest(new FfiRequest { ByteWriteToFile = req }); }
            catch { _client.CancelPendingCallback(asyncId); throw; }

            return tcs.Task;
        }

        public Task SendFileAsync(StreamSendFileRequest req)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (req.LocalParticipantHandle == 0)
                req.LocalParticipantHandle = _localParticipantHandle;

            var asyncId = req.InitializeRequestAsyncId();

            _client.RegisterPendingCallback<StreamSendFileCallback>(
                asyncId,
                e => e.MessageCase == FfiEvent.MessageOneofCase.SendFile ? e.SendFile : null,
                cb =>
                {
                    if (cb.Error != null)
                        tcs.TrySetException(new Exception(cb.Error.Description));
                    else
                    {
                        Console.WriteLine("File sent successfully");
                        tcs.TrySetResult(true);
                    }
                },
                onCancel: () => tcs.TrySetCanceled()
            );

            try { _client.SendRequest(new FfiRequest { SendFile = req }); }
            catch { _client.CancelPendingCallback(asyncId); throw; }

            return tcs.Task;
        }

        // ── Text stream ───────────────────────────────────────────────────────────

        public Task<ulong> OpenTextStreamAsync(StreamTextOptions options)
        {
            var tcs = new TaskCompletionSource<ulong>(TaskCreationOptions.RunContinuationsAsynchronously);

            var req = new TextStreamOpenRequest
            {
                LocalParticipantHandle = _localParticipantHandle,
                Options = options
            };
            var asyncId = req.InitializeRequestAsyncId();

            _client.RegisterPendingCallback<TextStreamOpenCallback>(
                asyncId,
                e => e.MessageCase == FfiEvent.MessageOneofCase.TextStreamOpen ? e.TextStreamOpen : null,
                cb =>
                {
                    if (cb.Error != null)
                        tcs.TrySetException(new Exception(cb.Error.Description));
                    else
                    {
                        Console.WriteLine($"Text stream opened: {cb.Writer.Handle.Id}");
                        tcs.TrySetResult(cb.Writer.Handle.Id);
                    }
                },
                onCancel: () => tcs.TrySetCanceled()
            );

            try { _client.SendRequest(new FfiRequest { TextStreamOpen = req }); }
            catch { _client.CancelPendingCallback(asyncId); throw; }

            return tcs.Task;
        }

        public Task WriteTextStreamAsync(ulong writerHandle, string text)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var req = new TextStreamWriterWriteRequest { WriterHandle = writerHandle, Text = text };
            var asyncId = req.InitializeRequestAsyncId();

            _client.RegisterPendingCallback<TextStreamWriterWriteCallback>(
                asyncId,
                e => e.MessageCase == FfiEvent.MessageOneofCase.TextStreamWriterWrite ? e.TextStreamWriterWrite : null,
                cb =>
                {
                    if (cb.Error != null)
                        tcs.TrySetException(new Exception(cb.Error.Description));
                    else
                    {
                        Console.WriteLine("Text stream write completed");
                        tcs.TrySetResult(true);
                    }
                },
                onCancel: () => tcs.TrySetCanceled()
            );

            try { _client.SendRequest(new FfiRequest { TextStreamWrite = req }); }
            catch { _client.CancelPendingCallback(asyncId); throw; }

            return tcs.Task;
        }

        public Task CloseTextStreamAsync(ulong writerHandle)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var req = new TextStreamWriterCloseRequest { WriterHandle = writerHandle };
            var asyncId = req.InitializeRequestAsyncId();

            _client.RegisterPendingCallback<TextStreamWriterCloseCallback>(
                asyncId,
                e => e.MessageCase == FfiEvent.MessageOneofCase.TextStreamWriterClose ? e.TextStreamWriterClose : null,
                cb =>
                {
                    if (cb.Error != null)
                        tcs.TrySetException(new Exception(cb.Error.Description));
                    else
                    {
                        Console.WriteLine("Text stream closed");
                        tcs.TrySetResult(true);
                    }
                },
                onCancel: () => tcs.TrySetCanceled()
            );

            try { _client.SendRequest(new FfiRequest { TextStreamClose = req }); }
            catch { _client.CancelPendingCallback(asyncId); throw; }

            return tcs.Task;
        }

        public Task<string> ReadAllTextAsync(ulong readerHandle)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            var req = new TextStreamReaderReadAllRequest { ReaderHandle = readerHandle };
            var asyncId = req.InitializeRequestAsyncId();

            _client.RegisterPendingCallback<TextStreamReaderReadAllCallback>(
                asyncId,
                e => e.MessageCase == FfiEvent.MessageOneofCase.TextStreamReaderReadAll ? e.TextStreamReaderReadAll : null,
                cb =>
                {
                    if (cb.Error != null)
                        tcs.TrySetException(new Exception(cb.Error.Description));
                    else
                    {
                        Console.WriteLine($"Text stream read all completed: {cb.Content.Length} characters");
                        tcs.TrySetResult(cb.Content);
                    }
                },
                onCancel: () => tcs.TrySetCanceled()
            );

            try { _client.SendRequest(new FfiRequest { TextReadAll = req }); }
            catch { _client.CancelPendingCallback(asyncId); throw; }

            return tcs.Task;
        }

        public Task SendTextAsync(StreamSendTextRequest req)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (req.LocalParticipantHandle == 0)
                req.LocalParticipantHandle = _localParticipantHandle;

            var asyncId = req.InitializeRequestAsyncId();

            _client.RegisterPendingCallback<StreamSendTextCallback>(
                asyncId,
                e => e.MessageCase == FfiEvent.MessageOneofCase.SendText ? e.SendText : null,
                cb =>
                {
                    if (cb.Error != null)
                        tcs.TrySetException(new Exception(cb.Error.Description));
                    else
                    {
                        Console.WriteLine("Text sent successfully");
                        tcs.TrySetResult(true);
                    }
                },
                onCancel: () => tcs.TrySetCanceled()
            );

            try { _client.SendRequest(new FfiRequest { SendText = req }); }
            catch { _client.CancelPendingCallback(asyncId); throw; }

            return tcs.Task;
        }

        // ── Streaming event handlers ──────────────────────────────────────────────

        private void OnByteStreamReaderEventReceived(ByteStreamReaderEvent e)
        {
            Console.WriteLine($"Byte stream reader event: {e.DetailCase}");
            ByteStreamReaderEvent?.Invoke(e);
        }

        private void OnTextStreamReaderEventReceived(TextStreamReaderEvent e)
        {
            Console.WriteLine($"Text stream reader event: {e.DetailCase}");
            TextStreamReaderEvent?.Invoke(e);
        }
    }
}
