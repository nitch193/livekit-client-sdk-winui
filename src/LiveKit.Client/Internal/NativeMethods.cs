using System;
using System.Runtime.InteropServices;


namespace LiveKit.Internal
{
    internal static class NativeMethods
    {
        const string Lib = "livekit_ffi";

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "livekit_ffi_drop_handle")]
        internal extern static bool FfiDropHandle(IntPtr handleId);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "livekit_ffi_request")]
        internal extern static unsafe IntPtr FfiNewRequest(byte* data, int len, out byte* dataPtr, out UIntPtr dataLen);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "livekit_ffi_initialize")]
        internal extern static IntPtr LiveKitInitialize(FFICallbackDelegate cb, bool captureLogs, string sdk, string sdkVersion);
    }
}
