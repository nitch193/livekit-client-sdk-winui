# LiveKit WinUI/WPF Client SDK

This project provides a C# Client SDK for LiveKit, designed for WinUI 3 and WPF applications. It wraps the [LiveKit Rust SDK](https://github.com/livekit/rust-sdks) using C-compatible FFI.

## Prerequisites

- **Rust**: Latest stable version (required to build the native library).
- **.NET 8 SDK**: For building the C# projects.

## Project Structure

- `src/LiveKit.Client`: The core C# Class Library containing the SDK logic.
- `src/LiveKit.Playground`: A Console Application for testing the SDK.
- `refs/`: Reference repositories (Rust SDK, Unity SDK).

## Building

### 1. Build the Native Library (Rust)

Navigate to `refs/rust-sdks` and build the `livekit-ffi` crate:

```bash
cd refs/rust-sdks
cargo build -p livekit-ffi --release
```

This will produce a shared library:
- Windows: `target/release/livekit_ffi.dll`
- Linux: `target/release/liblivekit_ffi.so`
- macOS: `target/release/liblivekit_ffi.dylib`

### 2. Build the C# Solution

```bash
dotnet build LiveKit.WinUI.sln
```

### 3. Run the Playground

You need to copy the built native library to the output directory of the Playground app.

**Windows Example:**
```powershell
copy refs\rust-sdks\target\release\livekit_ffi.dll src\LiveKit.Playground\bin\Debug\net8.0\
```

**Run:**
```bash
dotnet run --project src/LiveKit.Playground/LiveKit.Playground.csproj -- <URL> <TOKEN>
```

## Architecture

- **LiveKit.Client**: Contains the `Room` class and other high-level APIs.
- **Internal**:
    - `FfiClient`: Singleton that manages the FFI server.
    - `NativeMethods`: P/Invoke declarations.
    - `Proto`: Generated Protobuf classes (copied from Unity SDK).

## Status

- [x] Project Structure
- [x] FFI Bindings (Initial)
- [x] Basic Room Connection
- [ ] Video/Audio Rendering (WinUI specific)
- [ ] Full Event Handling
