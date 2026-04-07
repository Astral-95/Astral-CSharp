using Astral.Network.Channels;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Astral.Exceptions;

namespace Astral.Network.Transport.Bunches;
public class OutBunchPool
{
    readonly ConcurrentBag<OutBunch> Items = new ConcurrentBag<OutBunch>();


    public OutBunchPool(int NumBunches = 0)
    {
        for (int i = 0; i < NumBunches; i++)
        {
            var Bunch = new OutBunch(null, 64);
            Items.Add(Bunch);
            PooledObjectsTracker.Register<OutBunch>(Bunch);
        }
    }

    public OutBunch Rent<T>(NetaChannel Channel)
    {
        if (!Items.TryTake(out var Bunch))
        {
            Bunch = new OutBunch(Channel, 64);
            PooledObjectsTracker.OnNewPoolObject();
        }
        else
        {
            Bunch.InPool = 0;
            Bunch.ResetBunch(Channel, 64);
        }
#if NETA_DEBUG
        PooledObjectsTracker.Register<T>(Bunch);
#endif
        return Bunch;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Return<T>(OutBunch Bunch)
    {
#if NETA_DEBUG
        var Val = Interlocked.CompareExchange(ref Bunch.InPool, 1, 0);
        if (Val != 0) throw new AlreadyInPoolException($"{typeof(T).Name} Attempted to return a bunch that is already in the pool.");
        PooledObjectsTracker.Unregister(this);
#endif
        Bunch.Channel = null;
        Items.Add(Bunch);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Return(OutBunch Bunch) => Return<OutBunchPool>(Bunch);
}