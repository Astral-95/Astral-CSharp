using Astral.Diagnostics;
using System.Collections.Concurrent;

namespace Astral.Containers;

public class PooledConcurrentDictionary<TKey, TValue> : ConcurrentDictionary<TKey, TValue> where TKey : notnull
{
    protected int InPool = 0;
    private static readonly ConcurrentBag<PooledConcurrentDictionary<TKey, TValue>> Pool = new();

    PooledConcurrentDictionary() : base() { }
    PooledConcurrentDictionary(int ConcurrencyLevel, int Capacity) : base(ConcurrencyLevel, Capacity) { }
    public static int GetPoolSize() { return Pool.Count; }
    public static PooledConcurrentDictionary<TKey, TValue> Rent()
    {
        if (!Pool.TryTake(out var Container))
        {
            Container = new PooledConcurrentDictionary<TKey, TValue>();
            PooledObjectsTracker.OnNewPoolObject();
        }
        else
        {
            Container.InPool = 0;
            Container.Clear();
        }

#if CFG_DEBUG
        PooledObjectsTracker.Register(Container);
#endif
        return Container;
    }

    public static PooledConcurrentDictionary<TKey, TValue> Rent(int ConcurrencyLevel, int Capacity)
    {
        if (!Pool.TryTake(out var Container))
        {
            Container = new PooledConcurrentDictionary<TKey, TValue>(ConcurrencyLevel, Capacity);
            PooledObjectsTracker.OnNewPoolObject();
        }
        else
        {
            Container.InPool = 0;
            Container.Clear();
        }

#if CFG_DEBUG
        PooledObjectsTracker.Register(Container);
#endif
        return Container;
    }

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