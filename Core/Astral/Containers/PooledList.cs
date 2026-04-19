using Astral.Diagnostics;
using System.Collections.Concurrent;

namespace Astral.Containers;

public class PooledList<TElement> : List<TElement>
{
    protected int InPool = 0;

    [ThreadStatic]
    private static ObjectStack<PooledList<TElement>> Pool;

    PooledList(int Capacity) : base(Capacity) { }
    public static PooledList<TElement> Rent(int Capacity = 2)
    {
        if (Pool == null)
        {
            Pool = new ObjectStack<PooledList<TElement>>();
        }
        if (!Pool.Take(out var Container))
        {
            Container = new PooledList<TElement>(Capacity);
            PooledObjectsTracker.OnNewPoolObject();
        }
        else
        {
            Container.InPool = 0;
            Container.EnsureCapacity(Capacity);
        }
        return Container;
    }

    public static int GetPoolSize() { return Pool.Count; }

    public void Return()
    {
        var Val = Interlocked.CompareExchange(ref InPool, 1, 0);
#if CFG_DEBUG
        Guard.Assert(Val == 0, "Attempted to return an object that is already in the pool");
#endif
        Clear();
        Pool.Add(this);
    }
}