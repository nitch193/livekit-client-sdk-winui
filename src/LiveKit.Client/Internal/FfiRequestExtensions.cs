using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace LiveKit.Internal
{
    /// <summary>
    /// Extension helpers for stamping a client-generated <c>RequestAsyncId</c> onto
    /// outgoing FFI request objects before they cross the native boundary.
    ///
    /// The ID is generated atomically with <see cref="Interlocked.Increment"/> so
    /// every concurrent request gets a strictly unique, monotonically increasing value
    /// without any lock overhead.
    ///
    /// The property setter is discovered once per concrete request type via reflection
    /// and cached in a <see cref="ConcurrentDictionary{TKey,TValue}"/>, so the
    /// reflection cost is paid exactly once — all subsequent calls reuse the cached
    /// delegate.  This approach is safe on AOT/IL2CPP runtimes because it relies only
    /// on <see cref="PropertyInfo.SetValue"/> rather than expression compilation.
    /// </summary>
    internal static class FfiRequestExtensions
    {
        // Shared counter — Interlocked.Increment is a single CPU instruction (LOCK XADD),
        // so no lock is needed and the result is still strictly unique per call.
        private static long _nextRequestAsyncId;

        // One setter delegate per concrete protobuf request type.  GetOrAdd is atomic,
        // so two threads racing on the first request of a given type will not create
        // two separate delegates.
        private static readonly ConcurrentDictionary<Type, Action<object, ulong>?> _setterCache
            = new ConcurrentDictionary<Type, Action<object, ulong>?>();

        /// <summary>
        /// Assigns a fresh, globally unique <c>RequestAsyncId</c> to the request object
        /// (if that type exposes such a writable <c>ulong</c> property) and returns the
        /// same id so the caller can register a pending-callback entry before sending.
        ///
        /// Returns <c>0</c> when the type does not carry a <c>RequestAsyncId</c>
        /// property (fire-and-forget requests).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong InitializeRequestAsyncId<T>(this T request)
        {
            if (request == null) return 0;

            var setter = _setterCache.GetOrAdd(request.GetType(), static type =>
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
                var prop = type.GetProperty("RequestAsyncId", flags);

                if (prop == null || !prop.CanWrite || prop.PropertyType != typeof(ulong))
                    return null;

                return (target, value) => prop.SetValue(target, value);
            });

            if (setter == null) return 0;

            // Cast is safe: Interlocked.Increment returns a long; we only use the lower
            // 63 bits and ulong 0 is reserved for "no pending callback".
            var requestAsyncId = (ulong)Interlocked.Increment(ref _nextRequestAsyncId);
            setter(request, requestAsyncId);
            return requestAsyncId;
        }
    }
}
