namespace LiveKit.Internal.FFIClients.Requests
{
    /// <summary>
    /// Factory interface for creating pooled FFI request wrappers.
    /// </summary>
    public interface IFFIBridge
    {
        FfiRequestWrap<T> NewRequest<T>() where T : class, new();
    }
}
