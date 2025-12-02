using System;
using System.Threading.Tasks;
using LiveKit.Internal;
using LiveKit.Proto;

namespace LiveKit
{
    public class DataStream
    {
        private readonly FfiClient _client;
        private readonly ulong _localParticipantHandle;
        
        // TaskCompletionSources for async operations
        private TaskCompletionSource<byte[]>? _byteReadAllTcs;
        private TaskCompletionSource<string>? _byteWriteToFileTcs;
        private TaskCompletionSource<ulong>? _byteStreamOpenTcs;
        private TaskCompletionSource<bool>? _byteStreamWriteTcs;
        private TaskCompletionSource<bool>? _byteStreamCloseTcs;
        
        private TaskCompletionSource<string>? _textReadAllTcs;
        private TaskCompletionSource<ulong>? _textStreamOpenTcs;
        private TaskCompletionSource<bool>? _textStreamWriteTcs;
        private TaskCompletionSource<bool>? _textStreamCloseTcs;
        
        private TaskCompletionSource<bool>? _sendFileTcs;
        private TaskCompletionSource<bool>? _sendTextTcs;
        
        // Event handlers for stream events
        public event Action<ByteStreamReaderEvent>? ByteStreamReaderEvent;
        public event Action<TextStreamReaderEvent>? TextStreamReaderEvent;
        
        public DataStream(ulong localParticipantHandle)
        {
            _client = FfiClient.Instance;
            _localParticipantHandle = localParticipantHandle;
            
            // Subscribe to callbacks
            _client.ByteStreamReaderReadAllReceived += OnByteStreamReaderReadAllReceived;
            _client.ByteStreamReaderWriteToFileReceived += OnByteStreamReaderWriteToFileReceived;
            _client.ByteStreamOpenReceived += OnByteStreamOpenReceived;
            _client.ByteStreamWriterWriteReceived += OnByteStreamWriterWriteReceived;
            _client.ByteStreamWriterCloseReceived += OnByteStreamWriterCloseReceived;
            
            _client.TextStreamReaderReadAllReceived += OnTextStreamReaderReadAllReceived;
            _client.TextStreamOpenReceived += OnTextStreamOpenReceived;
            _client.TextStreamWriterWriteReceived += OnTextStreamWriterWriteReceived;
            _client.TextStreamWriterCloseReceived += OnTextStreamWriterCloseReceived;
            
            _client.SendFileReceived += OnSendFileReceived;
            _client.SendTextReceived += OnSendTextReceived;
            
            _client.ByteStreamReaderEventReceived += OnByteStreamReaderEventReceived;
            _client.TextStreamReaderEventReceived += OnTextStreamReaderEventReceived;
        }

        public Task<ulong> OpenByteStreamAsync(StreamByteOptions options)
        {
            _byteStreamOpenTcs = new TaskCompletionSource<ulong>();
            var request = new FfiRequest
            {
                ByteStreamOpen = new ByteStreamOpenRequest
                {
                    LocalParticipantHandle = _localParticipantHandle,
                    Options = options
                }
            };
            _client.SendRequest(request);
            return _byteStreamOpenTcs.Task;
        }

        public Task WriteByteStreamAsync(ulong writerHandle, byte[] data)
        {
            _byteStreamWriteTcs = new TaskCompletionSource<bool>();
            var request = new FfiRequest
            {
                ByteStreamWrite = new ByteStreamWriterWriteRequest
                {
                    WriterHandle = writerHandle,
                    Bytes = Google.Protobuf.ByteString.CopyFrom(data)
                }
            };
            _client.SendRequest(request);
            return _byteStreamWriteTcs.Task;
        }

        public Task CloseByteStreamAsync(ulong writerHandle)
        {
            _byteStreamCloseTcs = new TaskCompletionSource<bool>();
            var request = new FfiRequest
            {
                ByteStreamClose = new ByteStreamWriterCloseRequest
                {
                    WriterHandle = writerHandle
                }
            };
            _client.SendRequest(request);
            return _byteStreamCloseTcs.Task;
        }

        public Task<ulong> OpenTextStreamAsync(StreamTextOptions options)
        {
            _textStreamOpenTcs = new TaskCompletionSource<ulong>();
            var request = new FfiRequest
            {
                TextStreamOpen = new TextStreamOpenRequest
                {
                    LocalParticipantHandle = _localParticipantHandle,
                    Options = options
                }
            };
            _client.SendRequest(request);
            return _textStreamOpenTcs.Task;
        }

        public Task WriteTextStreamAsync(ulong writerHandle, string text)
        {
            _textStreamWriteTcs = new TaskCompletionSource<bool>();
            var request = new FfiRequest
            {
                TextStreamWrite = new TextStreamWriterWriteRequest
                {
                    WriterHandle = writerHandle,
                    Text = text
                }
            };
            _client.SendRequest(request);
            return _textStreamWriteTcs.Task;
        }

        public Task CloseTextStreamAsync(ulong writerHandle)
        {
            _textStreamCloseTcs = new TaskCompletionSource<bool>();
            var request = new FfiRequest
            {
                TextStreamClose = new TextStreamWriterCloseRequest
                {
                    WriterHandle = writerHandle
                }
            };
            _client.SendRequest(request);
            return _textStreamCloseTcs.Task;
        }

        public Task SendFileAsync(StreamSendFileRequest request)
        {
            _sendFileTcs = new TaskCompletionSource<bool>();
            var ffiRequest = new FfiRequest
            {
                SendFile = request
            };
            // Ensure local participant handle is set if not already
            if (request.LocalParticipantHandle == 0)
            {
                request.LocalParticipantHandle = _localParticipantHandle;
            }
            _client.SendRequest(ffiRequest);
            return _sendFileTcs.Task;
        }

        public Task SendTextAsync(StreamSendTextRequest request)
        {
            _sendTextTcs = new TaskCompletionSource<bool>();
            var ffiRequest = new FfiRequest
            {
                SendText = request
            };
            // Ensure local participant handle is set if not already
            if (request.LocalParticipantHandle == 0)
            {
                request.LocalParticipantHandle = _localParticipantHandle;
            }
            _client.SendRequest(ffiRequest);
            return _sendTextTcs.Task;
        }

        public Task<byte[]> ReadAllBytesAsync(ulong readerHandle)
        {
            _byteReadAllTcs = new TaskCompletionSource<byte[]>();
            var request = new FfiRequest
            {
                ByteReadAll = new ByteStreamReaderReadAllRequest
                {
                    ReaderHandle = readerHandle
                }
            };
            _client.SendRequest(request);
            return _byteReadAllTcs.Task;
        }

        public Task<string> WriteBytesToFileAsync(ulong readerHandle, string directory, string nameOverride = "")
        {
            _byteWriteToFileTcs = new TaskCompletionSource<string>();
            var request = new FfiRequest
            {
                ByteWriteToFile = new ByteStreamReaderWriteToFileRequest
                {
                    ReaderHandle = readerHandle,
                    Directory = directory,
                    NameOverride = nameOverride
                }
            };
            _client.SendRequest(request);
            return _byteWriteToFileTcs.Task;
        }

        public Task<string> ReadAllTextAsync(ulong readerHandle)
        {
            _textReadAllTcs = new TaskCompletionSource<string>();
            var request = new FfiRequest
            {
                TextReadAll = new TextStreamReaderReadAllRequest
                {
                    ReaderHandle = readerHandle
                }
            };
            _client.SendRequest(request);
            return _textReadAllTcs.Task;
        }
        
        // Byte Stream Reader Callbacks
        private void OnByteStreamReaderReadAllReceived(ByteStreamReaderReadAllCallback e)
        {
            if (e.Error != null)
            {
                _byteReadAllTcs?.TrySetException(new Exception(e.Error.Description));
            }
            else
            {
                _byteReadAllTcs?.TrySetResult(e.Content.ToByteArray());
                Console.WriteLine($"Byte stream read all completed: {e.Content.Length} bytes");
            }
        }

        private void OnByteStreamReaderWriteToFileReceived(ByteStreamReaderWriteToFileCallback e)
        {
            if (e.Error != null)
            {
                _byteWriteToFileTcs?.TrySetException(new Exception(e.Error.Description));
            }
            else
            {
                _byteWriteToFileTcs?.TrySetResult(e.FilePath);
                Console.WriteLine($"Byte stream written to file: {e.FilePath}");
            }
        }

        // Byte Stream Writer Callbacks
        private void OnByteStreamOpenReceived(ByteStreamOpenCallback e)
        {
            if (e.Error != null)
            {
                _byteStreamOpenTcs?.TrySetException(new Exception(e.Error.Description));
            }
            else
            {
                _byteStreamOpenTcs?.TrySetResult(e.Writer.Handle.Id);
                Console.WriteLine($"Byte stream opened: {e.Writer.Handle.Id}");
            }
        }

        private void OnByteStreamWriterWriteReceived(ByteStreamWriterWriteCallback e)
        {
            if (e.Error != null)
            {
                _byteStreamWriteTcs?.TrySetException(new Exception(e.Error.Description));
            }
            else
            {
                _byteStreamWriteTcs?.TrySetResult(true);
                Console.WriteLine("Byte stream write completed");
            }
        }

        private void OnByteStreamWriterCloseReceived(ByteStreamWriterCloseCallback e)
        {
            if (e.Error != null)
            {
                _byteStreamCloseTcs?.TrySetException(new Exception(e.Error.Description));
            }
            else
            {
                _byteStreamCloseTcs?.TrySetResult(true);
                Console.WriteLine("Byte stream closed");
            }
        }

        // Text Stream Reader Callbacks
        private void OnTextStreamReaderReadAllReceived(TextStreamReaderReadAllCallback e)
        {
            if (e.Error != null)
            {
                _textReadAllTcs?.TrySetException(new Exception(e.Error.Description));
            }
            else
            {
                _textReadAllTcs?.TrySetResult(e.Content);
                Console.WriteLine($"Text stream read all completed: {e.Content.Length} characters");
            }
        }

        // Text Stream Writer Callbacks
        private void OnTextStreamOpenReceived(TextStreamOpenCallback e)
        {
            if (e.Error != null)
            {
                _textStreamOpenTcs?.TrySetException(new Exception(e.Error.Description));
            }
            else
            {
                _textStreamOpenTcs?.TrySetResult(e.Writer.Handle.Id);
                Console.WriteLine($"Text stream opened: {e.Writer.Handle.Id}");
            }
        }

        private void OnTextStreamWriterWriteReceived(TextStreamWriterWriteCallback e)
        {
            if (e.Error != null)
            {
                _textStreamWriteTcs?.TrySetException(new Exception(e.Error.Description));
            }
            else
            {
                _textStreamWriteTcs?.TrySetResult(true);
                Console.WriteLine("Text stream write completed");
            }
        }

        private void OnTextStreamWriterCloseReceived(TextStreamWriterCloseCallback e)
        {
            if (e.Error != null)
            {
                _textStreamCloseTcs?.TrySetException(new Exception(e.Error.Description));
            }
            else
            {
                _textStreamCloseTcs?.TrySetResult(true);
                Console.WriteLine("Text stream closed");
            }
        }

        // File Transfer Callbacks
        private void OnSendFileReceived(StreamSendFileCallback e)
        {
            if (e.Error != null)
            {
                _sendFileTcs?.TrySetException(new Exception(e.Error.Description));
            }
            else
            {
                _sendFileTcs?.TrySetResult(true);
                Console.WriteLine("File sent successfully");
            }
        }

        private void OnSendTextReceived(StreamSendTextCallback e)
        {
            if (e.Error != null)
            {
                _sendTextTcs?.TrySetException(new Exception(e.Error.Description));
            }
            else
            {
                _sendTextTcs?.TrySetResult(true);
                Console.WriteLine("Text sent successfully");
            }
        }

        // Stream Event Callbacks
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
