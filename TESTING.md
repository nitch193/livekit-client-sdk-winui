# Testing Your LiveKit WinUI SDK

## Quick Start Options

### Option 1: LiveKit Cloud (Recommended for Quick Testing)

1. **Sign up**: Go to https://cloud.livekit.io/ and create a free account
2. **Create a project**: You'll get an API Key, API Secret, and WebSocket URL
3. **Generate a token** using their dashboard or CLI
4. **Run your app**:
   ```bash
   dotnet run --project src/LiveKit.Playground/LiveKit.Playground.csproj -- \
     "wss://your-project.livekit.cloud" "your-generated-token"
   ```

### Option 2: Local Server with Docker

1. **Install Docker**:
   ```bash
   sudo apt install docker.io
   sudo usermod -aG docker $USER
   # Log out and back in for group changes to take effect
   ```

2. **Run LiveKit Server**:
   ```bash
   docker run --rm -p 7880:7880 -p 7881:7881 -p 7882:7882/udp \
     -e LIVEKIT_KEYS="devkey: secret" \
     livekit/livekit-server --dev
   ```

3. **Generate a token**:
   ```bash
   # Install Python package
   pip install livekit
   
   # Generate token
   python generate_token.py my-room user1
   ```

4. **Run your app** with the generated URL and token

### Option 3: Use LiveKit Meet for Testing

You can also test by connecting to a room and having another participant join via LiveKit Meet:

1. Go to https://meet.livekit.io/
2. Create or join a room
3. Use the same room name and server URL in your C# app

## Testing the SDK

Once you have a server and token:

```bash
# Run the playground app
dotnet run --project src/LiveKit.Playground/LiveKit.Playground.csproj -- \
  "ws://localhost:7880" "your-token-here"
```

## Expected Output

If everything works, you should see:
```
LiveKit WinUI Playground
FFI Server Initialized
Connecting to ws://localhost:7880...
Connected to room: my-room
Room Event: ...
```

## Troubleshooting

- **"Name or service not known"**: Invalid URL or server not running
- **Connection timeout**: Check firewall settings or server status
- **Authentication failed**: Token is invalid or expired
- **FFI errors**: Make sure `liblivekit_ffi.so` is in the output directory

## Next Steps

After basic connectivity works:
1. Test with multiple participants
2. Implement video/audio track handling
3. Create a WinUI 3 GUI application
4. Add video rendering capabilities
