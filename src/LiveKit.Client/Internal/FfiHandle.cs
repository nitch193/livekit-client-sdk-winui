using System;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;

namespace LiveKit.Internal
{
    /// <summary>
    /// A SafeHandle wrapper around a LiveKit FFI native handle.
    ///
    /// Using SafeHandle integrates with the CLR's Constrained Execution Region (CER)
    /// infrastructure, which guarantees that ReleaseHandle (and thus FfiDropHandle) is
    /// called even under low-memory or stack-overflow conditions where a normal finalizer
    /// might be skipped. This prevents native handle leaks regardless of how the
    /// managed side is disposed.
    ///
    /// Callers that need the raw pointer to pass into a protobuf request must use
    /// DangerousGetHandle() — the "Dangerous" naming is intentional and warns that the
    /// caller is responsible for keeping this FfiHandle alive for the duration of the
    /// native call.
    /// </summary>
    public sealed class FfiHandle : SafeHandle
    {
        internal FfiHandle(IntPtr ptr) : base(IntPtr.Zero, ownsHandle: true)
        {
            SetHandle(ptr);
        }

        /// <summary>
        /// A handle is invalid if it is null or the conventional -1 sentinel Rust uses
        /// to indicate allocation failure.
        /// </summary>
        public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);

        /// <summary>
        /// Called by the CLR finalizer / SafeHandle infrastructure.
        /// The ReliabilityContract tells the CER that this method will not corrupt
        /// state even if it only partially executes (MayFail).
        /// </summary>
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        protected override bool ReleaseHandle()
        {
            return NativeMethods.FfiDropHandle(handle);
        }

        /// <summary>
        /// Convenience factory that converts a protobuf <see cref="Proto.FfiOwnedHandle"/>
        /// (which carries the handle id as a ulong) into a managed FfiHandle.
        /// </summary>
        public static FfiHandle FromOwnedHandle(Proto.FfiOwnedHandle handle)
        {
            return new FfiHandle((IntPtr)handle.Id);
        }
    }
}
