using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using LiveKit.Proto;
using Microsoft.UI.Dispatching;

namespace LiveKit.Internal
{
    /// <summary>
    /// Central FFI hub.  Owns the native callback, routes events safely to the UI
    /// thread, and manages the lifetime of all in-flight async requests.
    ///
    /// ── Thread model ────────────────────────────────────────────────────────────
    ///
    ///   FFICallback       runs on Rust's native thread
    ///   AudioStreamEvent  delivered directly on the native thread (fast-path)
    ///   Everything else   enqueued to the UI DispatcherQueue captured at Initialize()
    ///
    /// ── Race model for one-shot async requests ──────────────────────────────────
    ///
    ///   1. Caller stamps a unique RequestAsyncId on the protobuf request object.
    ///   2. Caller calls RegisterPendingCallback() — BEFORE crossing the FFI boundary.
    ///   3. Caller sends the request.
    ///   4. Rust echoes the same id back through FfiEvent.{Callback}.AsyncId.
    ///   5. FFICallback dispatches to the UI thread; DispatchEvent calls
    ///      TryDispatchPendingCallback which removes the entry with TryRemove.
    ///   6. Only the side that wins TryRemove is allowed to invoke completion/cancel.
    ///      This gives at-most-once guarantee with no additional locking.
    ///
    /// ── Memory model for SendRequest ────────────────────────────────────────────
    ///
    ///   Serialization buffer is rented from ArrayPool&lt;byte&gt;.Shared (zero allocation
    ///   per call), pinned with 'fixed' only for the duration of the native call,
    ///   then returned deterministically in a finally block.
    /// </summary>
    internal sealed class FfiClient : IDisposable
    {
        private static readonly Lazy<FfiClient> _instance = new(() => new FfiClient());
        public static FfiClient Instance => _instance.Value;

        // Volatile so the FFICallback guard read is guaranteed to see the latest write
        // without a full memory barrier on every callback invocation.
        private volatile bool _isDisposed;
        private static bool _initialized;

        // DispatcherQueue for the UI thread — captured once in Initialize().
        //
        // When null (console apps, test hosts, any non-WinUI3 context) callbacks
        // are invoked directly on the Rust thread.  Handlers must be thread-safe
        // in that case, but the library still functions correctly.
        private DispatcherQueue? _dispatcherQueue;

        // True when we have a real DispatcherQueue to post to.
        private bool _hasDispatcherQueue;

        // ── Pending one-shot callbacks ───────────────────────────────────────────
        //
        // Thread-safety / race model (see class doc above):
        //   • registration (TryAdd)  happens before the request is sent to Rust
        //   • completion   (TryRemove) and cancellation (TryRemove) are symmetric;
        //     exactly one side wins, which prevents double-complete / double-cancel
        //   • ConcurrentDictionary gives atomic add/remove per entry; no cross-entry
        //     transaction is needed
        private readonly ConcurrentDictionary<ulong, PendingCallbackBase> _pendingCallbacks = new();

        // ── Push / streaming events (not one-shot, stay as events) ──────────────
        public event Action<RoomEvent>?          RoomEventReceived;
        public event Action<TrackEvent>?         TrackEventReceived;
        public event Action<VideoStreamEvent>?   VideoStreamEventReceived;
        public event Action<AudioStreamEvent>?   AudioStreamEventReceived;
        public event Action<RpcMethodInvocationEvent>? RpcMethodInvocationReceived;
        public event Action<ByteStreamReaderEvent>?    ByteStreamReaderEventReceived;
        public event Action<TextStreamReaderEvent>?    TextStreamReaderEventReceived;

        private FfiClient() { }

        // ── Lifecycle ────────────────────────────────────────────────────────────

        /// <summary>
        /// Initializes the native LiveKit SDK.
        ///
        /// When called from a WinUI3 UI thread, the current <see cref="DispatcherQueue"/>
        /// is captured and all callbacks (except audio) are posted to it — keeping
        /// all event handling on the UI thread.
        ///
        /// When called from a plain console thread (no DispatcherQueue), callbacks are
        /// invoked directly on the Rust native thread.  Handlers must be thread-safe
        /// in that mode, but the SDK will work correctly.
        /// </summary>
        public void Initialize()
        {
            if (_initialized) return;

            // Try to capture the DispatcherQueue of the calling thread.
            // Returns null on console / background threads — that is acceptable.
            try
            {
                _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
                _hasDispatcherQueue = _dispatcherQueue != null;
            }
            catch
            {
                // DispatcherQueue type may not be available at all in some host environments.
                _dispatcherQueue = null;
                _hasDispatcherQueue = false;
            }

            if (!_hasDispatcherQueue)
            {
                Console.WriteLine("[LiveKit] No DispatcherQueue found — callbacks will be invoked directly on the Rust thread. Ensure handlers are thread-safe.");
            }

            NativeMethods.LiveKitInitialize(FFICallback, captureLogs: true, sdk: "csharp", sdkVersion: "0.1.0");
            _initialized = true;
            Console.WriteLine("FFI Server Initialized");
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            // Stop all rooms on the native side first.
            SendRequest(new FfiRequest { Dispose = new DisposeRequest() });

            // Cancel every in-flight task so callers don't hang.
            ClearPendingCallbacks();
            Console.WriteLine("FFI Server Disposed");
        }

        // ── Pending callback registration / dispatch ─────────────────────────────

        /// <summary>
        /// Registers a one-shot completion handler keyed by <paramref name="requestAsyncId"/>.
        /// Must be called BEFORE the corresponding request is sent to Rust.
        /// </summary>
        internal void RegisterPendingCallback<TCallback>(
            ulong requestAsyncId,
            Func<FfiEvent, TCallback?> selector,
            Action<TCallback> onComplete,
            Action? onCancel = null) where TCallback : class
        {
            if (requestAsyncId == 0) return; // fire-and-forget request type — no callback expected

            var pending = new PendingCallback<TCallback>(selector, onComplete, onCancel);
            if (!_pendingCallbacks.TryAdd(requestAsyncId, pending))
            {
                // Two requests sharing the same id would corrupt each other's completion.
                throw new InvalidOperationException(
                    $"Duplicate pending callback for RequestAsyncId={requestAsyncId}. " +
                    "This is a bug: IDs must be unique.");
            }
        }

        /// <summary>
        /// Cancels a pending callback.  Uses TryRemove so it is safe to call even if
        /// the Rust callback already arrived and won the TryRemove race first.
        /// </summary>
        internal bool CancelPendingCallback(ulong requestAsyncId)
        {
            if (requestAsyncId == 0) return false;
            if (_pendingCallbacks.TryRemove(requestAsyncId, out var pending))
            {
                pending.Cancel();
                return true;
            }
            return false;
        }

        private void ClearPendingCallbacks()
        {
            // Snapshot Keys: _isDisposed was set to true before this call, so no new
            // native callbacks will be enqueued.  Each cancellation still validates
            // ownership via TryRemove before invoking OnCancel.
            foreach (var id in _pendingCallbacks.Keys)
                CancelPendingCallback(id);
        }

        // ── SendRequest ──────────────────────────────────────────────────────────

        /// <summary>
        /// Serializes <paramref name="request"/> into a pooled byte buffer, calls the
        /// native FFI, parses the synchronous response, and returns the buffer to the
        /// pool — all within a single stack frame so no GC allocation occurs for the
        /// buffer itself.
        /// </summary>
        public FfiResponse SendRequest(FfiRequest request)
        {
            int size = request.CalculateSize();
            // Rent from the shared system pool — well-tuned and allocation-free.
            byte[] buffer = ArrayPool<byte>.Shared.Rent(size);
            try
            {
                unsafe
                {
                    request.WriteTo(new Span<byte>(buffer, 0, size));
                    fixed (byte* requestDataPtr = buffer)
                    {
                        var handle = NativeMethods.FfiNewRequest(
                            requestDataPtr,
                            size,
                            out byte* dataPtr,
                            out UIntPtr dataLen);

                        var dataSpan = new Span<byte>(dataPtr, (int)dataLen.ToUInt64());
                        var response = FfiResponse.Parser.ParseFrom(dataSpan);
                        NativeMethods.FfiDropHandle(handle);
                        return response;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[LiveKit] SendRequest failed: {ex}");
                throw new Exception("Cannot send FFI request", ex);
            }
            finally
            {
                // Return the rented buffer unconditionally — even if parsing throws.
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        // ── Native callback ──────────────────────────────────────────────────────

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FFICallbackDelegate(UIntPtr data, UIntPtr size);

        // Keep a strong reference to the delegate so the GC cannot collect it while
        // the native side still holds a function pointer to it.
        private static readonly FFICallbackDelegate _callbackDelegate = FFICallback;

        private static unsafe void FFICallback(UIntPtr data, UIntPtr size)
        {
            // Guard: disposed or not yet initialized — drop everything.
            if (Instance._isDisposed || Instance._dispatcherQueue == null) return;

            var respData = new Span<byte>(data.ToPointer(), (int)size.ToUInt64());
            var ffiEvent = FfiEvent.Parser.ParseFrom(respData);

            // Audio stream events are delivered directly on the Rust thread.
            // Audio consumers run on their own dedicated thread and must not wait
            // for the next UI frame, so this bypass is intentional.
            if (ffiEvent.MessageCase == FfiEvent.MessageOneofCase.AudioStreamEvent)
            {
                Instance.AudioStreamEventReceived?.Invoke(ffiEvent.AudioStreamEvent);
                return;
            }

            // Route all other events.
            if (Instance._hasDispatcherQueue && Instance._dispatcherQueue != null)
            {
                // WinUI3 host: post to the UI thread's message loop (FIFO, preserves ordering).
                Instance._dispatcherQueue.TryEnqueue(() =>
                {
                    if (!Instance._isDisposed)
                        DispatchEvent(ffiEvent);
                });
            }
            else
            {
                // Console / non-WinUI3 host: invoke directly on the Rust callback thread.
                // Handlers must be thread-safe. This matches the pre-refactor behaviour
                // that the Playground relied on.
                if (!Instance._isDisposed)
                    DispatchEvent(ffiEvent);
            }
        }

        // ── Event dispatch ───────────────────────────────────────────────────────

        private static void DispatchEvent(FfiEvent ffiEvent)
        {
            // First: try to complete a one-shot async request by AsyncId.
            var asyncId = ExtractRequestAsyncId(ffiEvent);
            if (asyncId.HasValue && Instance.TryDispatchPendingCallback(asyncId.Value, ffiEvent))
            {
                // One-shot — do not also fire the general event switch below.
                return;
            }

            // Second: fire general push / streaming events.
            switch (ffiEvent.MessageCase)
            {
                case FfiEvent.MessageOneofCase.RoomEvent:
                    Instance.RoomEventReceived?.Invoke(ffiEvent.RoomEvent);
                    break;
                case FfiEvent.MessageOneofCase.TrackEvent:
                    Instance.TrackEventReceived?.Invoke(ffiEvent.TrackEvent);
                    break;
                case FfiEvent.MessageOneofCase.VideoStreamEvent:
                    Instance.VideoStreamEventReceived?.Invoke(ffiEvent.VideoStreamEvent);
                    break;
                case FfiEvent.MessageOneofCase.AudioStreamEvent:
                    Instance.AudioStreamEventReceived?.Invoke(ffiEvent.AudioStreamEvent);
                    break;
                case FfiEvent.MessageOneofCase.RpcMethodInvocation:
                    Instance.RpcMethodInvocationReceived?.Invoke(ffiEvent.RpcMethodInvocation);
                    break;
                case FfiEvent.MessageOneofCase.ByteStreamReaderEvent:
                    Instance.ByteStreamReaderEventReceived?.Invoke(ffiEvent.ByteStreamReaderEvent);
                    break;
                case FfiEvent.MessageOneofCase.TextStreamReaderEvent:
                    Instance.TextStreamReaderEventReceived?.Invoke(ffiEvent.TextStreamReaderEvent);
                    break;
                case FfiEvent.MessageOneofCase.Logs:
                    // Log batch — silently consumed.
                    break;
                case FfiEvent.MessageOneofCase.Panic:
                    Console.Error.WriteLine($"[LiveKit] Rust panic received");
                    break;
                default:
                    break;
            }
        }

        // ── AsyncId extraction ───────────────────────────────────────────────────

        /// <summary>
        /// Maps each one-shot async callback <see cref="FfiEvent.MessageCase"/> to the
        /// <c>AsyncId</c> it carries.  Streaming / push events return <c>null</c> and
        /// are excluded intentionally — they are not modeled as pending one-shot
        /// completions.
        /// </summary>
        private static ulong? ExtractRequestAsyncId(FfiEvent ffiEvent)
        {
            return ffiEvent.MessageCase switch
            {
                FfiEvent.MessageOneofCase.Connect              => ffiEvent.Connect?.AsyncId,
                FfiEvent.MessageOneofCase.PublishTrack         => ffiEvent.PublishTrack?.AsyncId,
                FfiEvent.MessageOneofCase.UnpublishTrack       => ffiEvent.UnpublishTrack?.AsyncId,
                FfiEvent.MessageOneofCase.SetLocalMetadata     => ffiEvent.SetLocalMetadata?.AsyncId,
                FfiEvent.MessageOneofCase.SetLocalName         => ffiEvent.SetLocalName?.AsyncId,
                FfiEvent.MessageOneofCase.SetLocalAttributes   => ffiEvent.SetLocalAttributes?.AsyncId,
                FfiEvent.MessageOneofCase.GetStats             => ffiEvent.GetStats?.AsyncId,
                FfiEvent.MessageOneofCase.GetSessionStats      => ffiEvent.GetSessionStats?.AsyncId,
                FfiEvent.MessageOneofCase.CaptureAudioFrame    => ffiEvent.CaptureAudioFrame?.AsyncId,
                FfiEvent.MessageOneofCase.PerformRpc           => ffiEvent.PerformRpc?.AsyncId,
                FfiEvent.MessageOneofCase.ByteStreamReaderReadAll    => ffiEvent.ByteStreamReaderReadAll?.AsyncId,
                FfiEvent.MessageOneofCase.ByteStreamReaderWriteToFile => ffiEvent.ByteStreamReaderWriteToFile?.AsyncId,
                FfiEvent.MessageOneofCase.ByteStreamOpen       => ffiEvent.ByteStreamOpen?.AsyncId,
                FfiEvent.MessageOneofCase.ByteStreamWriterWrite => ffiEvent.ByteStreamWriterWrite?.AsyncId,
                FfiEvent.MessageOneofCase.ByteStreamWriterClose => ffiEvent.ByteStreamWriterClose?.AsyncId,
                FfiEvent.MessageOneofCase.SendFile             => ffiEvent.SendFile?.AsyncId,
                FfiEvent.MessageOneofCase.TextStreamReaderReadAll => ffiEvent.TextStreamReaderReadAll?.AsyncId,
                FfiEvent.MessageOneofCase.TextStreamOpen       => ffiEvent.TextStreamOpen?.AsyncId,
                FfiEvent.MessageOneofCase.TextStreamWriterWrite => ffiEvent.TextStreamWriterWrite?.AsyncId,
                FfiEvent.MessageOneofCase.TextStreamWriterClose => ffiEvent.TextStreamWriterClose?.AsyncId,
                FfiEvent.MessageOneofCase.SendText             => ffiEvent.SendText?.AsyncId,
                FfiEvent.MessageOneofCase.SendBytes            => ffiEvent.SendBytes?.AsyncId,
                _ => null,
            };
        }

        // ── TryDispatch — at-most-once ownership transfer ────────────────────────

        private bool TryDispatchPendingCallback(ulong asyncId, FfiEvent ffiEvent)
        {
            // Remove-first is the key race-proofing step:
            //   • If cancellation already won TryRemove, this returns false → no-op.
            //   • If we win, cancellation later sees no entry → also a no-op.
            // Either way, completion fires at most once.
            if (!_pendingCallbacks.TryRemove(asyncId, out var pending))
                return false;

            if (pending.TryComplete(ffiEvent))
                return true;

            // Defensive re-insertion: selector returned null (type mismatch).
            // In normal operation this branch is never hit.  Re-insert rather than
            // silently dropping the entry.
            if (!_pendingCallbacks.TryAdd(asyncId, pending))
                pending.Cancel(); // lost the re-insertion race → cancel to avoid leak

            return false;
        }

        // ── Inner pending-callback types ─────────────────────────────────────────

        private abstract class PendingCallbackBase
        {
            public abstract bool TryComplete(FfiEvent ffiEvent);
            public abstract void Cancel();
        }

        private sealed class PendingCallback<TCallback> : PendingCallbackBase
            where TCallback : class
        {
            private readonly Func<FfiEvent, TCallback?> _selector;
            private readonly Action<TCallback>          _onComplete;
            private readonly Action?                    _onCancel;

            public PendingCallback(
                Func<FfiEvent, TCallback?> selector,
                Action<TCallback>          onComplete,
                Action?                    onCancel)
            {
                _selector   = selector;
                _onComplete = onComplete;
                _onCancel   = onCancel;
            }

            // Runs on the UI DispatcherQueue thread (same as the old event-based path).
            public override bool TryComplete(FfiEvent ffiEvent)
            {
                var cb = _selector(ffiEvent);
                if (cb == null) return false;
                _onComplete(cb);
                return true;
            }

            public override void Cancel() => _onCancel?.Invoke();
        }
    }
}
