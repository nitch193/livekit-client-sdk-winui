using System;
using System.Threading.Tasks;
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

        public Room()
        {
            _client = FfiClient.Instance;
            _client.Initialize();
            _client.ConnectReceived += OnConnectReceived;
            _client.DisconnectReceived += OnDisconnectReceived;
            _client.RoomEventReceived += OnRoomEventReceived;
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
        public async Task<TrackPublication> PublishTrackAsync(LocalVideoTrack track)
        {
            if (_localParticipantHandle == 0)
                throw new InvalidOperationException("Not connected to a room");

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

            // For now, return a simple wrapper. In a full implementation,
            // we'd wait for the PublishTrackCallback
            await Task.Delay(100); // Give it time to publish
            return new TrackPublication(response.PublishTrack.AsyncId);
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
        }
    }
}
