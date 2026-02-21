using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LiveKit;
using LiveKit.Proto;

class Program
{
    // Replace with valid URL and Token
    const string URL = "wss://test-project-znb5n0y7.livekit.cloud";
    const string TOKEN = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJleHAiOjE3NjU0OTc4NDQsImlkZW50aXR5Ijoibml0ZXNoIiwiaXNzIjoiQVBJbVI5a1NRTlRqaXpoIiwibmJmIjoxNzY1NDgyODQ0LCJzdWIiOiJuaXRlc2giLCJ2aWRlbyI6eyJjYW5QdWJsaXNoIjp0cnVlLCJjYW5QdWJsaXNoRGF0YSI6dHJ1ZSwiY2FuU3Vic2NyaWJlIjp0cnVlLCJyb29tIjoidGVzdC1yb29tIiwicm9vbUpvaW4iOnRydWV9fQ.BwGwRsX4juL8kRyTuNrtwZ0tnSWSlnQxLwlxGLehRvM";

    static Room? _roomInstance;
    static readonly HashSet<string> _teachers = new HashSet<string>();
    static readonly Dictionary<string, TrackSource> _trackSources = new Dictionary<string, TrackSource>();

    static async Task Main(string[] args)
    {
        Console.WriteLine("Starting LiveKit Playground - Teacher Screen Logger");

        string url = URL;
        string token = TOKEN;

        if (args.Length >= 2)
        {
            url = args[0];
            token = args[1];
        }

        _roomInstance = new Room();
        _roomInstance.RoomEventReceived += OnRoomEventReceived;
        _roomInstance.VideoStreamEventReceived += OnVideoStreamEventReceived;

        try
        {
            Console.WriteLine($"Connecting to {url}...");
            await _roomInstance.ConnectAsync(url, token);
            Console.WriteLine("Connected to room!");

            // Create a screen capturer and publish (optional, but keeps playground functional)
            Console.WriteLine("Starting local screen capture...");
            var track = LocalVideoTrack.CreateScreenShareTrack();
            var capturer = new ScreenCapturer(track.Source);
            capturer.Start();

            var options = new TrackPublishOptions { Simulcast = true };
            var publication = await _roomInstance.PublishTrackAsync(track, options);
            Console.WriteLine($"Published local screen share with SID: {publication.Info.Sid}");

            Console.WriteLine("Listening for teacher screen shares... Press Enter to exit.");
            Console.ReadLine();

            capturer.Dispose();
            track.Dispose();
            await _roomInstance.DisconnectAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
        }
    }

    private static void OnRoomEventReceived(RoomEvent e)
    {
        switch (e.MessageCase)
        {
            case RoomEvent.MessageOneofCase.ParticipantConnected:
                // ParticipantConnected.Info is OwnedParticipant, which has Info (ParticipantInfo)
                HandleParticipantConnected(e.ParticipantConnected.Info.Info);
                break;
            case RoomEvent.MessageOneofCase.ParticipantsUpdated:
                foreach (var p in e.ParticipantsUpdated.Participants)
                {
                    HandleParticipantConnected(p);
                }
                break;
            case RoomEvent.MessageOneofCase.TrackPublished:
                HandleTrackPublished(e.TrackPublished);
                break;
            case RoomEvent.MessageOneofCase.TrackSubscribed:
                HandleTrackSubscribed(e.TrackSubscribed);
                break;
        }
    }

    private static void HandleParticipantConnected(ParticipantInfo info)
    {
        // Check metadata for "teacher" role
        if (!string.IsNullOrEmpty(info.Metadata) && info.Metadata.Contains("teacher"))
        {
            Console.WriteLine($"Teacher detected: {info.Identity}");
            lock (_teachers)
            {
                _teachers.Add(info.Identity);
            }
        }
    }

    private static void HandleTrackPublished(TrackPublished e)
    {
        if (e.Publication != null && e.Publication.Info != null)
        {
            lock (_trackSources)
            {
                _trackSources[e.Publication.Info.Sid] = e.Publication.Info.Source;
            }
        }
    }

    private static void HandleTrackSubscribed(TrackSubscribed e)
    {
        bool isTeacher;
        lock (_teachers)
        {
            isTeacher = _teachers.Contains(e.ParticipantIdentity);
        }

        if (isTeacher)
        {
            TrackSource source = TrackSource.SourceUnknown;
            lock (_trackSources)
            {
                if (_trackSources.TryGetValue(e.Track.Info.Sid, out var s))
                {
                    source = s;
                }
            }

            // Fallback: Check name if source is unknown or not found
            if (source == TrackSource.SourceScreenshare || e.Track.Info.Name.Contains("screen"))
            {
                Console.WriteLine($"Subscribed to teacher's screen share: {e.Track.Info.Sid}");
                // Request video stream
                if (_roomInstance != null)
                {
                    // Fire and forget
                    _ = RequestTeacherStreamAsync(e.Track.Handle.Id);
                }
            }
        }
    }

    private static async Task RequestTeacherStreamAsync(ulong trackHandle)
    {
        try
        {
            if (_roomInstance != null)
            {
                await _roomInstance.GetVideoStreamAsync(trackHandle);
                Console.WriteLine($"Requested video stream for track handle: {trackHandle}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to request video stream: {ex.Message}");
        }
    }

    private static void OnVideoStreamEventReceived(VideoStreamEvent e)
    {
        if (e.FrameReceived != null)
        {
            var buffer = e.FrameReceived.Buffer;
            // Log frame data
            Console.WriteLine($"[Teacher Screen] Frame received: {buffer.Info.Width}x{buffer.Info.Height}, Timestamp: {e.FrameReceived.TimestampUs}");

            /* 
             * Usage with SwapChainVideoRenderer (in a WinUI Window/Page):
             * 
             * 1. Initialize the renderer with your local SwapChainPanel:
             *    _videoRenderer = new SwapChainVideoRenderer(mySwapChainPanel);
             * 
             * 2. In OnVideoStreamEventReceived:
             *    _videoRenderer.Render(e.FrameReceived.Buffer.Info);
             * 
             * 3. Dispose when done:
             *    _videoRenderer.Dispose();
             */
        }
    }
}
