using Astral.Diagnostics;
using System.Collections.Concurrent;

namespace Astral.Containers;

public class PooledList<TElement> : List<TElement>
{
    protected int InPool = 0;

    private static readonly ConcurrentBag<PooledList<TElement>> Pool = new();

    PooledList(int Capacity) : base(Capacity) { }
    public static PooledList<TElement> Rent(int Capacity = 2)
    {
        if (!Pool.TryTake(out var Container))
        {
            Container = new PooledList<TElement>(Capacity);
            PooledObjectsTracker.OnNewPoolObject();
        }
        else
        {
            Container.InPool = 0;
            Container.EnsureCapacity(Capacity);
        }
#if CFG_DEBUG
        PooledObjectsTracker.Register(Container);
#endif
        return Container;
    }

    public static int GetPoolSize() { return Pool.Count; }

    public void Return()
    {
        var Val = Interlocked.CompareExchange(ref InPool, 1, 0);
#if CFG_DEBUG
        Guard.Assert(Val == 0, "Attempted to return an object that is already in the pool");
        PooledObjectsTracker.Unregister(this);
#endif
        Clear();
        Pool.Add(this);
    }
}