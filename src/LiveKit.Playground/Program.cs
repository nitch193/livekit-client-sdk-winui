using System;
using System.Threading.Tasks;
using LiveKit;
using LiveKit.Proto;
using DataStream = LiveKit.DataStream;

class Program
{
    static Room? room;
    static DataStream? dataStream;

    static async Task Main(string[] args)
    {
        Console.WriteLine("LiveKit WinUI Playground - Event Listener Mode");
        
        room = new Room();
        
        // Replace with valid URL and Token
        string url = "wss://test-project-znb5n0y7.livekit.cloud";
        string token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJleHAiOjE3NjQ3MDA0MjQsImlkZW50aXR5Ijoibml0ZXNoIiwiaXNzIjoiQVBJbVI5a1NRTlRqaXpoIiwibmJmIjoxNzY0Njk5NTI0LCJzdWIiOiJuaXRlc2giLCJ2aWRlbyI6eyJjYW5QdWJsaXNoIjp0cnVlLCJjYW5QdWJsaXNoRGF0YSI6dHJ1ZSwiY2FuU3Vic2NyaWJlIjp0cnVlLCJyb29tIjoidGVzdC1yb29tIiwicm9vbUpvaW4iOnRydWV9fQ.7j1NTGTWBdclCDZPIosm5uaMEcYmJ0G_NnTY3Of0BgI";

        if (args.Length >= 2)
        {
            url = args[0];
            token = args[1];
        }

        try
        {
            Console.WriteLine($"Connecting to {url}...");
            await room.ConnectAsync(url, token);
            Console.WriteLine("Successfully connected!");

            // Initialize DataStream
            dataStream = new DataStream(room.LocalParticipantHandle);
            
            // Subscribe to events
            dataStream.TextStreamReaderEvent += OnTextStreamReceived;
            dataStream.ByteStreamReaderEvent += OnByteStreamReceived;

            room.RoomEvent += OnRoomEvent;
            room.TrackEvent += OnTrackEvent;
            room.DataReceived += OnDataReceived;
            room.ChatMessageReceived += OnChatMessageReceived;
            room.VideoStreamEvent += OnVideoStreamEvent;
            room.AudioStreamEvent += OnAudioStreamEvent;
            
            Console.WriteLine("DataStream initialized. Listening for events...");

            // Start Screen Capture
            Console.WriteLine("Starting Screen Capture...");
            var track = LocalVideoTrack.CreateScreenShareTrack();
            var capturer = new ScreenCapturer(track.Source);
            capturer.Start();
            
            await room.PublishTrackAsync(track);
            Console.WriteLine("Screen Share Track Published!");
            
            Console.WriteLine("Press Enter to disconnect and exit...");
            
            // Keep the application running until user presses Enter
            Console.ReadLine();

            capturer.Dispose();
            track.Dispose();
            await room.DisconnectAsync();
            Console.WriteLine("Disconnected.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    static void OnTextStreamReceived(TextStreamReaderEvent e)
    {
        Console.WriteLine($"[Text Stream Event] {e}");
        // You can inspect e.Content, e.Topic, etc. here depending on the specific event type
    }

    static void OnByteStreamReceived(ByteStreamReaderEvent e)
    {
        Console.WriteLine($"[Byte Stream Event] {e.DetailCase}");
        
        if (e.DetailCase == ByteStreamReaderEvent.DetailOneofCase.ChunkReceived)
        {
            var content = e.ChunkReceived.Content.ToByteArray();
            Console.WriteLine($"Received chunk: {content.Length} bytes");
        //If you know it's text, you can convert it:
            var text = System.Text.Encoding.UTF8.GetString(content);
            Console.WriteLine($"Content: {text}");
        }
        else if (e.DetailCase == ByteStreamReaderEvent.DetailOneofCase.Eos)
        {
            Console.WriteLine("End of stream received");
        }
    }

    static void OnRoomEvent(RoomEvent e)
    {
        Console.WriteLine($"[Room Event] {e.MessageCase}");
    }

    static void OnTrackEvent(TrackEvent e)
    {
        Console.WriteLine($"[Track Event] Received");
    }

    static void OnDataReceived(DataPacketReceived e)
    {
        Console.WriteLine($"[Data Received] Kind: {e.Kind}, Topic: {e.User.Topic}");
    }

    static void OnChatMessageReceived(ChatMessageReceived e)
    {
        Console.WriteLine($"[Chat Message] From: {e.ParticipantIdentity}, Message: {e.Message.Message}");
    }

    static void OnVideoStreamEvent(VideoStreamEvent e)
    {
        Console.WriteLine($"[Video Stream Event] {e.MessageCase}");
    }

    static void OnAudioStreamEvent(AudioStreamEvent e)
    {
        Console.WriteLine($"[Audio Stream Event] {e.MessageCase}");
    }
}
