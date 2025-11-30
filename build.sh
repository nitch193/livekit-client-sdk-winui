#!/bin/bash
set -e

echo "Building Rust SDK..."
cd refs/rust-sdks
# Ensure we are using a recent cargo if possible, or warn user
RUST_BACKTRACE=full cargo build -p livekit-ffi --release
cd ../..

echo "Building C# Solution..."
dotnet build LiveKit.WinUI.sln

echo "Copying Native Library..."
# Create output dir if it doesn't exist
mkdir -p src/LiveKit.Playground/bin/Debug/net8.0/

# Detect OS and copy appropriate file
if [[ "$OSTYPE" == "linux-gnu"* ]]; then
    if [ -f "refs/rust-sdks/target/release/liblivekit_ffi.so" ]; then
        cp refs/rust-sdks/target/release/liblivekit_ffi.so src/LiveKit.Playground/bin/Debug/net8.0/
        echo "Copied liblivekit_ffi.so"
    else
        echo "Warning: liblivekit_ffi.so not found. Build might have failed."
    fi
elif [[ "$OSTYPE" == "msys" ]] || [[ "$OSTYPE" == "cygwin" ]] || [[ "$OSTYPE" == "win32" ]]; then
    if [ -f "refs/rust-sdks/target/release/livekit_ffi.dll" ]; then
        cp refs/rust-sdks/target/release/livekit_ffi.dll src/LiveKit.Playground/bin/Debug/net8.0/
        echo "Copied livekit_ffi.dll"
    else
        echo "Warning: livekit_ffi.dll not found."
    fi
else
    echo "Unknown OS, please copy the native library manually."
fi

echo "Done! You can run the playground with:"
echo "dotnet run --project src/LiveKit.Playground/LiveKit.Playground.csproj -- <URL> <TOKEN>"
