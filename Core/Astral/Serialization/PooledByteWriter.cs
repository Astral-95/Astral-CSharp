using Astral.Diagnostics;
using System.Collections.Concurrent;
using Astral.Exceptions;

namespace Astral.Serialization;

public class PooledByteWriter : ByteWriter
{
    protected int InPool = 0;
    private static readonly ConcurrentBag<PooledByteWriter> Pool = new();


#pragma warning disable CS8618
    protected PooledByteWriter() { }
    protected PooledByteWriter(Int32 InitialSizeBytes = 128) : base(InitialSizeBytes) { }
#pragma warning restore CS8618

    public static PooledByteWriter Rent(Int32 MinBytes = 64)
    {
        if (!Pool.TryTake(out var Writer))
        {
            Writer = new PooledByteWriter(MinBytes);
        }
        else
        {
            Writer.InPool = 0;
            Writer.Reset(MinBytes);
        }

#if CFG_DEBUG
        PooledObjectsTracker.Register(Writer);
#endif
        return Writer;
    }

    public static PooledByteWriter Rent<T>(Int32 MinBytes = 64)
    {
        if (!Pool.TryTake(out var Writer))
        {
            Writer = new PooledByteWriter(MinBytes);
        }
        else
        {
            Writer.InPool = 0;
            Writer.Reset(MinBytes);
        }

#if CFG_DEBUG
        PooledObjectsTracker.Register<T>(Writer);
#endif
        return Writer;
    }

    public void Return()
    {
#if CFG_DEBUG
        var Val = Interlocked.CompareExchange(ref InPool, 1, 0);
        Guard.DebugAssert(Val == 0);
        PooledObjectsTracker.Unregister(this);
#endif
        Pool.Add(this);
    }

    public void Return<T>()
    {
#if CFG_DEBUG
        var Val = Interlocked.CompareExchange(ref InPool, 1, 0);
        if (Val != 0) throw new AlreadyInPoolException($"{typeof(T).Name} Attempted to return a writer that is already in the pool.");
        PooledObjectsTracker.Unregister(this);
#endif
        Pool.Add(this);
    }
}