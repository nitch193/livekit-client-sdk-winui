using System;
using System.Threading.Tasks;
using LiveKit;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("LiveKit WinUI Playground");
        
        var room = new Room();
        
        // Replace with valid URL and Token
        string url = "wss://test-project-znb5n0y7.livekit.cloud";
        string token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJleHAiOjE3NjQ1MzIwMzAsImlkZW50aXR5IjoibWF4IiwiaXNzIjoiQVBJbVI5a1NRTlRqaXpoIiwibmJmIjoxNzY0NTMxMTMwLCJzdWIiOiJtYXgiLCJ2aWRlbyI6eyJjYW5QdWJsaXNoIjp0cnVlLCJjYW5QdWJsaXNoRGF0YSI6dHJ1ZSwiY2FuU3Vic2NyaWJlIjp0cnVlLCJyb29tIjoidGVzdC1yb29tIiwicm9vbUpvaW4iOnRydWV9fQ.Qz56HI3q9yCg3wf8xNICf0UbhORjlittp_vXFm7zbOw";

        if (args.Length >= 2)
        {
            url = args[0];
            token = args[1];
        }

        Console.WriteLine($"Connecting to {url}...");
        try 
        {
            // Note: This will fail if the native library is not found or if URL/Token are invalid.
            await room.ConnectAsync(url, token);
            Console.WriteLine("Successfully connected!");
            
            // Start screen sharing
            Console.WriteLine("Starting screen share...");
            var screenTrack = LocalVideoTrack.CreateScreenShareTrack(1280, 720);
            var capturer = new ScreenCapturer(screenTrack.Source, 1280, 720, fps: 15);
            
            try
            {
                await room.PublishTrackAsync(screenTrack);
                Console.WriteLine("Screen share track published!");
                
                capturer.Start();
                Console.WriteLine("Screen capture started. Sending test pattern...");
                
                Console.WriteLine("Press Enter to stop screen sharing and disconnect...");
                Console.ReadLine();
                
                await capturer.StopAsync();
                Console.WriteLine("Screen capture stopped");
            }
            finally
            {
                screenTrack.Dispose();
                capturer.Dispose();
            }
            
            await room.DisconnectAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}
