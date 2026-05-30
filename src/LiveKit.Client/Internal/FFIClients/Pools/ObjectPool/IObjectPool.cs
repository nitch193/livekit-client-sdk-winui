namespace LiveKit.Internal.FFIClients.Pools.ObjectPool
{
    public interface IObjectPool<T> where T : class
    {
        T Get();
        void Release(T element);
        void Clear();
        int CountInactive { get; }
    }
}
