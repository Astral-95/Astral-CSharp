using Astral.Exceptions;
using System.Collections.Concurrent;

namespace Astral.Network.Serialization;

public sealed class PooledNetByteWriter : NetByteWriter
{
    int InPool = 0;
    private static readonly ConcurrentBag<PooledNetByteWriter> Pool = new ConcurrentBag<PooledNetByteWriter>();

#pragma warning disable CS8618
    PooledNetByteWriter(Int32 InitialSizeBits = 512) : base(InitialSizeBits) { }
#pragma warning restore CS8618

    public static PooledNetByteWriter Rent(Int32 MinBits = 512)
    {
        if (!Pool.TryTake(out var Writer))
        {
            Writer = new PooledNetByteWriter(MinBits);
            PooledObjectsTracker.OnNewPoolObject();
        }
        else
        {
            Writer.InPool = 0;
            Writer.Reset(MinBits);
        }

#if NETA_DEBUG
        PooledObjectsTracker.Register(Writer);
#endif

        return Writer;
    }

    public static long GetPoolSize() { return Pool.Count; }

    public void Return()
    {
#if NETA_DEBUG
        var Val = Interlocked.CompareExchange(ref InPool, 1, 0);
        if (Val != 0) throw new AlreadyInPoolException("Attempted to return a writer that is already in the pool.");
        PooledObjectsTracker.Unregister(this);
#endif
        Pool.Add(this);
    }
}