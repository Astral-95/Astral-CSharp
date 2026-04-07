using Astral.Network.Channels;
using Astral.Network.Enums;
using Astral.Network.Serialization;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Astral.Exceptions;

namespace Astral.Network.Transport;

public class OutBunch : NetByteWriter
{
    internal protected int InPool = 0;
    //private static readonly AtomicStack<OutBunch> Pool = new AtomicStack<OutBunch>(1024);
    private static readonly ConcurrentBag<OutBunch> Pool = new ConcurrentBag<OutBunch>();

    static public int NumTnstantiated = 0;
    public NetaChannel? Channel { get; internal set; }
    public Neta_BunchIdType Id { get; private set; } = 0;
    public EBunchFlags Flags { get; internal set; }

    public int HeaderBytes { get; internal set; } = sizeof(Neta_BunchIdType) + sizeof(Neta_ChannelIndexType) + sizeof(Neta_ChannelFlagsType);

    public int StateId { get; internal set; } = 0;

    static OutBunch()
    {
        //PrePopulate();
    }

    OutBunch()
    {

    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal OutBunch(NetaChannel Channel, int NumBytes = 128) : base(NumBytes)
    {
        //Writer.Serialize(Id);
        //Writer.WriteBit(Ordered);
        //Writer.Serialize(Channel.ChannelIndex, 8);
        Interlocked.Increment(ref NumTnstantiated);
        this.Channel = Channel;
        Serialize(Id);
        Neta_ChannelIndexType ChannelIndex = 0;
        Serialize(Flags);
        Serialize(ChannelIndex);
    }

    public static void PrePopulate(int Num)
    {
        for (int i = 0; i < Num; i++)
        {
            var Bunch = new OutBunch(null, 64);
            Pool.Add(Bunch);
            PooledObjectsTracker.OnNewPoolObject();
        }
    }

    public static long GetPoolSize() { return Pool.Count; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ResetBunch(NetaChannel Channel, int NumBytes = 128)
    {
        this.Channel = Channel;

        Id = 0;
        ReferencedObjects.Clear();
        Flags = EBunchFlags.None;
        HeaderBytes = sizeof(Neta_BunchIdType) + sizeof(Neta_ChannelIndexType) + sizeof(Neta_ChannelFlagsType);
        Reset(NumBytes);
        //SetPos(11);
        SetPos(sizeof(Neta_BunchIdType) + sizeof(Neta_ChannelIndexType) + sizeof(Neta_ChannelFlagsType));
    }

    public static OutBunch Rent<T>(NetaChannel Channel)
    {
        if (!Pool.TryTake(out var Bunch))
        {
            Bunch = new OutBunch(Channel, 64);
            PooledObjectsTracker.OnNewPoolObject();
        }
        else
        {
            Bunch.InPool = 0;
            Bunch.ResetBunch(Channel, 64);
        }
        return Bunch;
    }

    public void SetIsReliable()
    {
        Flags |= EBunchFlags.Reliable;
    }

    public void SetIsOrdered()
    {
        Flags |= EBunchFlags.Ordered;
    }

    internal void FinalizeBunch()
    {
        var OldNum = (Neta_BunchSizeType)Pos;

        SetPos(0);

        if ((Flags & EBunchFlags.Ordered) != 0)
        {
            if ((Flags & EBunchFlags.Reliable) == 0) throw new InvalidOperationException("Unreliable ordered bunch is not supported.");
        }

        Serialize(Id);
        Serialize(Channel!.ChannelIndex, 1);
        SetPos(OldNum);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Return<T>()
    {
#if NETA_DEBUG
        var Val = Interlocked.CompareExchange(ref InPool, 1, 0);
        if (Val != 0) throw new AlreadyInPoolException($"{typeof(T).Name} Attempted to return a bunch that is already in the pool.");
#endif
        Channel = null;
        Pool.Add(this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Return() => Return<OutBunch>();
}