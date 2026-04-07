using Astral.Diagnostics;

namespace Astral.Containers;

public class PooledDictionary<TKey, TValue> : Dictionary<TKey, TValue> where TKey : notnull
{
    protected int InPool = 0;
    private static readonly ConcurrentStore<PooledDictionary<TKey, TValue>> Pool = new();

    PooledDictionary(int Capacity) : base(Capacity) { }
    public static PooledDictionary<TKey, TValue> Rent(int Capacity)
    {
        if (!Pool.Take(out var Container))
        {
            Container = new PooledDictionary<TKey, TValue>(Capacity);
            PooledObjectsTracker.OnNewPoolObject();
        }
        else
        {
            Container.InPool = 0;
            Container.Clear();
            Container.EnsureCapacity(Capacity);
        }

        PooledObjectsTracker.Register(Container);
        return Container;
    }
    public static int GetPoolSize() { return Pool.Count; }
    public void Return()
    {
#if CFG_DEBUG
        var Val = Interlocked.CompareExchange(ref InPool, 1, 0);
        Guard.Assert(Val == 0, "Attempted to return an object that is already in the pool");
        PooledObjectsTracker.Unregister(this);
#endif
        Pool.Add(this);
    }
}