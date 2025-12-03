using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using LiveKit.Internal;
using LiveKit.Proto;

namespace LiveKit
{
    public class Room
    {
        private readonly FfiClient _client;
        private TaskCompletionSource<bool>? _connectTcs;
        private ulong _roomHandle;
        private ulong _localParticipantHandle;

        public ulong LocalParticipantHandle => _localParticipantHandle;

        public event Action<RoomEvent>? RoomEvent;
        public event Action<TrackEvent>? TrackEvent;
        public event Action<RpcMethodInvocationEvent>? RpcMethodInvocationEvent;
        public event Action<VideoStreamEvent>? VideoStreamEvent;
        public event Action<AudioStreamEvent>? AudioStreamEvent;
        public event Action<PerformRpcCallback>? PerformRpcCompleted;
        public event Action<GetSessionStatsCallback>? SessionStatsReceived;
        public event Action<DataPacketReceived>? DataReceived;
        public event Action<ChatMessageReceived>? ChatMessageReceived;

        private TaskCompletionSource<TrackPublication>? _publishTrackTcs;
        private TaskCompletionSource<bool>? _unpublishTrackTcs;
        private TaskCompletionSource<bool>? _setMetadataTcs;
        private TaskCompletionSource<bool>? _setNameTcs;
        private TaskCompletionSource<bool>? _setAttributesTcs;

        public Room()
        {
            _client = FfiClient.Instance;
            _client.Initialize();
            _client.ConnectReceived += OnConnectReceived;
            _client.DisconnectReceived += OnDisconnectReceived;
            _client.RoomEventReceived += OnRoomEventReceived;
            _client.PublishTrackReceived += OnPublishTrackReceived;
            _client.UnpublishTrackReceived += OnUnpublishTrackReceived;
            _client.TrackEventReceived += OnTrackEventReceived;
            _client.SetLocalMetadataReceived += OnSetLocalMetadataReceived;
            _client.SetLocalNameReceived += OnSetLocalNameReceived;
            _client.SetLocalAttributesReceived += OnSetLocalAttributesReceived;
            _client.GetSessionStatsReceived += OnGetSessionStatsReceived;
            _client.PerformRpcReceived += OnPerformRpcReceived;
            _client.RpcMethodInvocationReceived += OnRpcMethodInvocationReceived;
            _client.VideoStreamEventReceived += OnVideoStreamEventReceived;
            _client.AudioStreamEventReceived += OnAudioStreamEventReceived;
        }

        public Task ConnectAsync(string url, string token)
        {
            _connectTcs = new TaskCompletionSource<bool>();

            var request = new FfiRequest
            {
                Connect = new ConnectRequest
                {
                    Url = url,
                    Token = token
                }
            };

            var response = _client.SendRequest(request);
            
            if (response.Connect != null)
            {
                if (response.Connect.AsyncId == 0)
                {
                   // Handle synchronous failure if applicable, though AsyncId 0 usually means invalid request or similar.
                }
            }

            return _connectTcs.Task;
        }

        public Task DisconnectAsync()
        {
             var request = new FfiRequest
            {
                Disconnect = new DisconnectRequest
                {
                    RoomHandle = _roomHandle
                }
            };
            _client.SendRequest(request);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Publishes a local video track to the room.
        /// </summary>
        public Task<TrackPublication> PublishTrackAsync(LocalVideoTrack track)
        {
            if (_localParticipantHandle == 0)
                throw new InvalidOperationException("Not connected to a room");

            _publishTrackTcs = new TaskCompletionSource<TrackPublication>();

            var request = new FfiRequest
            {
                PublishTrack = new PublishTrackRequest
                {
                    LocalParticipantHandle = _localParticipantHandle,
                    TrackHandle = track.TrackHandle,
                    Options = new TrackPublishOptions
                    {
                        Source = TrackSource.SourceScreenshare
                    }
                }
            };

            var response = _client.SendRequest(request);
            
            if (response.PublishTrack == null)
            {
                throw new Exception("Failed to publish track");
            }

            return _publishTrackTcs.Task;
        }

        public Task<bool> UnpublishTrackAsync(TrackPublication publication)
        {
            if (_localParticipantHandle == 0)
                throw new InvalidOperationException("Not connected to a room");

            _unpublishTrackTcs = new TaskCompletionSource<bool>();

            var request = new FfiRequest
            {
                UnpublishTrack = new UnpublishTrackRequest
                {
                    LocalParticipantHandle = _localParticipantHandle,
                    TrackSid = publication.Sid
                }
            };
            _client.SendRequest(request);
            return _unpublishTrackTcs.Task;
        }

        public Task<bool> SetLocalMetadataAsync(string metadata)
        {
            _setMetadataTcs = new TaskCompletionSource<bool>();
            var request = new FfiRequest
            {
                SetLocalMetadata = new SetLocalMetadataRequest
                {
                    LocalParticipantHandle = _localParticipantHandle,
                    Metadata = metadata
                }
            };
            _client.SendRequest(request);
            return _setMetadataTcs.Task;
        }

        public Task<bool> SetLocalNameAsync(string name)
        {
            _setNameTcs = new TaskCompletionSource<bool>();
            var request = new FfiRequest
            {
                SetLocalName = new SetLocalNameRequest
                {
                    LocalParticipantHandle = _localParticipantHandle,
                    Name = name
                }
            };
            _client.SendRequest(request);
            return _setNameTcs.Task;
        }

        public Task<bool> SetLocalAttributesAsync(Dictionary<string, string> attributes)
        {
            _setAttributesTcs = new TaskCompletionSource<bool>();
            var request = new FfiRequest
            {
                SetLocalAttributes = new SetLocalAttributesRequest
                {
                    LocalParticipantHandle = _localParticipantHandle,
                }
            };
            foreach (var kvp in attributes)
            {
                request.SetLocalAttributes.Attributes.Add(new AttributesEntry { Key = kvp.Key, Value = kvp.Value });
            }
            _client.SendRequest(request);
            return _setAttributesTcs.Task;
        }

        private void OnConnectReceived(ConnectCallback e)
        {
            if (!string.IsNullOrEmpty(e.Error))
            {
                _connectTcs?.TrySetException(new Exception(e.Error));
            }
            else
            {
                // Store handles for later use
                _roomHandle = e.Result.Room.Handle.Id;
                _localParticipantHandle = e.Result.LocalParticipant.Handle.Id;
                
                _connectTcs?.TrySetResult(true);
                Console.WriteLine($"Connected to room: {e.Result.Room.Info.Name}");
            }
        }

        private void OnDisconnectReceived(DisconnectCallback e)
        {
            Console.WriteLine("Disconnected");
        }

        private void OnRoomEventReceived(RoomEvent e)
        {
            Console.WriteLine($"Room Event: {e.MessageCase}");
            if (e.ParticipantConnected != null)
            {
                Console.WriteLine($"Participant Connected: {e.ParticipantConnected.Info.Info.Identity}");
            }
            
            if (e.DataPacketReceived != null)
            {
                DataReceived?.Invoke(e.DataPacketReceived);
            }

            if (e.ChatMessage != null)
            {
                ChatMessageReceived?.Invoke(e.ChatMessage);
            }

            RoomEvent?.Invoke(e);
        }

        private void OnPublishTrackReceived(PublishTrackCallback e)
        {
            if (!string.IsNullOrEmpty(e.Error))
            {
                _publishTrackTcs?.TrySetException(new Exception(e.Error));
            }
            else
            {
                var publication = new TrackPublication(e.AsyncId, e.Publication.Info.Sid);
                _publishTrackTcs?.TrySetResult(publication);
                Console.WriteLine($"Track published successfully, Sid: {e.Publication.Info.Sid}");
            }
        }

        private void OnUnpublishTrackReceived(UnpublishTrackCallback e)
        {
            if (!string.IsNullOrEmpty(e.Error))
            {
                _unpublishTrackTcs?.TrySetException(new Exception(e.Error));
            }
            else
            {
                _unpublishTrackTcs?.TrySetResult(true);
                Console.WriteLine("Track unpublished");
            }
        }

        private void OnTrackEventReceived(TrackEvent e)
        {
            Console.WriteLine($"Track event received");
            TrackEvent?.Invoke(e);
        }

        private void OnSetLocalMetadataReceived(SetLocalMetadataCallback e)
        {
            if (!string.IsNullOrEmpty(e.Error))
            {
                _setMetadataTcs?.TrySetException(new Exception(e.Error));
            }
            else
            {
                _setMetadataTcs?.TrySetResult(true);
                Console.WriteLine("Local metadata set");
            }
        }

        private void OnSetLocalNameReceived(SetLocalNameCallback e)
        {
            if (!string.IsNullOrEmpty(e.Error))
            {
                _setNameTcs?.TrySetException(new Exception(e.Error));
            }
            else
            {
                _setNameTcs?.TrySetResult(true);
                Console.WriteLine("Local name set");
            }
        }

        private void OnSetLocalAttributesReceived(SetLocalAttributesCallback e)
        {
            if (!string.IsNullOrEmpty(e.Error))
            {
                _setAttributesTcs?.TrySetException(new Exception(e.Error));
            }
            else
            {
                _setAttributesTcs?.TrySetResult(true);
                Console.WriteLine("Local attributes set");
            }
        }

        private void OnGetSessionStatsReceived(GetSessionStatsCallback e)
        {
            Console.WriteLine("Session stats received");
            SessionStatsReceived?.Invoke(e);
        }

        private void OnPerformRpcReceived(PerformRpcCallback e)
        {
            Console.WriteLine($"RPC completed: {e.AsyncId}");
            PerformRpcCompleted?.Invoke(e);
        }

        private void OnRpcMethodInvocationReceived(RpcMethodInvocationEvent e)
        {
            Console.WriteLine($"RPC method invocation: {e.InvocationId}");
            RpcMethodInvocationEvent?.Invoke(e);
        }

        private void OnVideoStreamEventReceived(VideoStreamEvent e)
        {
            Console.WriteLine($"Video stream event: {e.MessageCase}");
            VideoStreamEvent?.Invoke(e);
        }

        private void OnAudioStreamEventReceived(AudioStreamEvent e)
        {
            Console.WriteLine($"Audio stream event: {e.MessageCase}");
            AudioStreamEvent?.Invoke(e);
        }
    }
}
