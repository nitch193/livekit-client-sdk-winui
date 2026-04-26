using System;
using System.Threading.Tasks;
using LiveKit.Internal;
using LiveKit.Proto;

namespace LiveKit
{
    public class Room
    {
        private readonly FfiClient _client;
        private ulong _roomHandle;
        private ulong _localParticipantHandle;

        public ulong LocalParticipantHandle => _localParticipantHandle;

        // Push / streaming events that the app layer subscribes to.
        public event Action<RoomEvent>?        RoomEventReceived;
        public event Action<VideoStreamEvent>? VideoStreamEventReceived;

        public Room()
        {
            _client = FfiClient.Instance;
            _client.RoomEventReceived           += OnRoomEventReceived;
            _client.TrackEventReceived          += OnTrackEventReceived;
            _client.VideoStreamEventReceived    += OnVideoStreamEventReceived;
            _client.RpcMethodInvocationReceived += OnRpcMethodInvocationReceived;
        }

        // ── Connect ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Connects to a LiveKit room and awaits the async response from Rust.
        ///
        /// Safety invariant: the pending callback is registered BEFORE
        /// <see cref="FfiClient.SendRequest"/> is called, which prevents the race
        /// where Rust emits the callback before Unity has a slot ready for it.
        ///
        /// Each call creates its own <see cref="TaskCompletionSource{T}"/> so two
        /// simultaneous calls never share the same slot (unlike the old single-field
        /// pattern that silently overwrote the previous TCS).
        /// </summary>
        public Task ConnectAsync(string url, string token)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var connectRequest = new ConnectRequest { Url = url, Token = token };

            // 1. Generate a unique async id and stamp it on the request.
            var asyncId = connectRequest.InitializeRequestAsyncId();

            // 2. Register the completion slot BEFORE sending.
            _client.RegisterPendingCallback<ConnectCallback>(
                asyncId,
                e => e.MessageCase == FfiEvent.MessageOneofCase.Connect ? e.Connect : null,
                cb =>
                {
                    if (!string.IsNullOrEmpty(cb.Error))
                    {
                        tcs.TrySetException(new Exception(cb.Error));
                    }
                    else
                    {
                        _roomHandle              = cb.Result.Room.Handle.Id;
                        _localParticipantHandle  = cb.Result.LocalParticipant.Handle.Id;
                        Console.WriteLine($"Connected to room: {cb.Result.Room.Info.Name}");
                        tcs.TrySetResult(true);
                    }
                },
                onCancel: () => tcs.TrySetCanceled()
            );

            // 3. Send after registration — Rust can now arrive any time, slot is ready.
            var request = new FfiRequest { Connect = connectRequest };
            try
            {
                _client.SendRequest(request);
            }
            catch
            {
                // SendRequest failed before Rust had a chance to echo the id back.
                // Cancel the pending slot so the task doesn't hang.
                _client.CancelPendingCallback(asyncId);
                throw;
            }

            return tcs.Task;
        }

        // ── Disconnect ───────────────────────────────────────────────────────────

        public Task DisconnectAsync()
        {
            var request = new FfiRequest
            {
                Disconnect = new DisconnectRequest { RoomHandle = _roomHandle }
            };
            _client.SendRequest(request);
            return Task.CompletedTask;
        }

        // ── Publish track ────────────────────────────────────────────────────────

        /// <summary>
        /// Publishes a local video track and awaits the async confirmation from Rust.
        /// Each call owns its own TCS — concurrent publish calls are now safe.
        /// </summary>
        public Task<TrackPublication> PublishTrackAsync(
            LocalVideoTrack track,
            TrackPublishOptions? options = null)
        {
            if (_localParticipantHandle == 0)
                throw new InvalidOperationException("Not connected to a room");

            var tcs = new TaskCompletionSource<TrackPublication>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var publishOptions = options ?? new TrackPublishOptions();
            if (publishOptions.Source == TrackSource.SourceUnknown)
                publishOptions.Source = TrackSource.SourceScreenshare;

            var publishRequest = new PublishTrackRequest
            {
                LocalParticipantHandle = _localParticipantHandle,
                TrackHandle            = track.TrackHandle,
                Options                = publishOptions
            };

            var asyncId = publishRequest.InitializeRequestAsyncId();

            _client.RegisterPendingCallback<PublishTrackCallback>(
                asyncId,
                e => e.MessageCase == FfiEvent.MessageOneofCase.PublishTrack ? e.PublishTrack : null,
                cb =>
                {
                    if (!string.IsNullOrEmpty(cb.Error))
                        tcs.TrySetException(new Exception(cb.Error));
                    else
                    {
                        Console.WriteLine("Track published successfully");
                        tcs.TrySetResult(new TrackPublication(cb.AsyncId, cb.Publication.Info));
                    }
                },
                onCancel: () => tcs.TrySetCanceled()
            );

            var request = new FfiRequest { PublishTrack = publishRequest };
            try
            {
                var response = _client.SendRequest(request);
                if (response.PublishTrack == null)
                    throw new Exception("Failed to publish track: empty response");
            }
            catch
            {
                _client.CancelPendingCallback(asyncId);
                throw;
            }

            return tcs.Task;
        }

        // ── Unpublish track ──────────────────────────────────────────────────────

        public Task UnpublishTrackAsync(ulong trackSid)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var unpublishRequest = new UnpublishTrackRequest
            {
                LocalParticipantHandle = _localParticipantHandle,
                TrackSid               = trackSid.ToString()
            };

            var asyncId = unpublishRequest.InitializeRequestAsyncId();

            _client.RegisterPendingCallback<UnpublishTrackCallback>(
                asyncId,
                e => e.MessageCase == FfiEvent.MessageOneofCase.UnpublishTrack ? e.UnpublishTrack : null,
                cb =>
                {
                    if (!string.IsNullOrEmpty(cb.Error))
                        tcs.TrySetException(new Exception(cb.Error));
                    else
                    {
                        Console.WriteLine("Track unpublished");
                        tcs.TrySetResult(true);
                    }
                },
                onCancel: () => tcs.TrySetCanceled()
            );

            var request = new FfiRequest { UnpublishTrack = unpublishRequest };
            try { _client.SendRequest(request); }
            catch { _client.CancelPendingCallback(asyncId); throw; }

            return tcs.Task;
        }

        // ── Video stream ─────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a video stream for the given remote track handle.
        /// This is a synchronous round-trip (response arrives inside SendRequest).
        /// </summary>
        public Task<OwnedVideoStream> GetVideoStreamAsync(
            ulong trackHandle,
            VideoStreamType type = VideoStreamType.VideoStreamNative)
        {
            var request = new FfiRequest
            {
                NewVideoStream = new NewVideoStreamRequest
                {
                    TrackHandle = trackHandle,
                    Type        = type,
                    Format      = VideoBufferType.Rgba
                }
            };

            var response = _client.SendRequest(request);

            if (response.NewVideoStream?.Stream == null)
                throw new Exception("Failed to create video stream");

            return Task.FromResult(response.NewVideoStream.Stream);
        }

        // ── Set local metadata ───────────────────────────────────────────────────

        public Task SetLocalMetadataAsync(string metadata)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var metadataRequest = new SetLocalMetadataRequest
            {
                LocalParticipantHandle = _localParticipantHandle,
                Metadata               = metadata
            };

            var asyncId = metadataRequest.InitializeRequestAsyncId();

            _client.RegisterPendingCallback<SetLocalMetadataCallback>(
                asyncId,
                e => e.MessageCase == FfiEvent.MessageOneofCase.SetLocalMetadata ? e.SetLocalMetadata : null,
                cb =>
                {
                    if (!string.IsNullOrEmpty(cb.Error))
                        tcs.TrySetException(new Exception(cb.Error));
                    else
                        tcs.TrySetResult(true);
                },
                onCancel: () => tcs.TrySetCanceled()
            );

            try { _client.SendRequest(new FfiRequest { SetLocalMetadata = metadataRequest }); }
            catch { _client.CancelPendingCallback(asyncId); throw; }

            return tcs.Task;
        }

        // ── Set local name ───────────────────────────────────────────────────────

        public Task SetLocalNameAsync(string name)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var nameRequest = new SetLocalNameRequest
            {
                LocalParticipantHandle = _localParticipantHandle,
                Name                   = name
            };

            var asyncId = nameRequest.InitializeRequestAsyncId();

            _client.RegisterPendingCallback<SetLocalNameCallback>(
                asyncId,
                e => e.MessageCase == FfiEvent.MessageOneofCase.SetLocalName ? e.SetLocalName : null,
                cb =>
                {
                    if (!string.IsNullOrEmpty(cb.Error))
                        tcs.TrySetException(new Exception(cb.Error));
                    else
                        tcs.TrySetResult(true);
                },
                onCancel: () => tcs.TrySetCanceled()
            );

            try { _client.SendRequest(new FfiRequest { SetLocalName = nameRequest }); }
            catch { _client.CancelPendingCallback(asyncId); throw; }

            return tcs.Task;
        }

        // ── General event handlers ───────────────────────────────────────────────

        private void OnRoomEventReceived(RoomEvent e)
        {
            if (e.ParticipantConnected != null)
                Console.WriteLine($"Participant Connected: {e.ParticipantConnected.Info.Info.Identity}");

            RoomEventReceived?.Invoke(e);
        }

        private void OnVideoStreamEventReceived(VideoStreamEvent e)
        {
            VideoStreamEventReceived?.Invoke(e);
        }

        private void OnTrackEventReceived(TrackEvent e)
        {
            Console.WriteLine("Track event received");
        }

        private void OnRpcMethodInvocationReceived(RpcMethodInvocationEvent e)
        {
            Console.WriteLine($"RPC method invocation: {e.InvocationId}");
        }
    }
}
