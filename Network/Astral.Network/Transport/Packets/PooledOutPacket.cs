using Astral.Containers;
using Astral.Network.Enums;
using System.Runtime.CompilerServices;
using Astral.Exceptions;

namespace Astral.Network.Transport;

public class PooledOutPacket : OutPacket
{
    private static readonly ConcurrentStore<PooledOutPacket> Pool = new ConcurrentStore<PooledOutPacket>();

    public int Retries = 0;
    public long DeadlineTicks = 0;

    public static long GetPoolSize() { return Pool.Count; }
    protected void ResetPacket(Neta_PacketIdType Id, EProtocolMessage Message)
    {
        base.Id = Id;
        HeaderBytes = NetaConsts.HeaderSizeBytes;
        this.Message = Message;
        Reset(NetaConsts.BufferMaxSizeBytes);
        FinalizeCalled = false;
        Flags = EPacketFlags.None;

        Serialize<Neta_PacketSizeType>(0);
        Serialize(base.Id);
        Serialize(Flags, 1);
        Serialize(Message, 1);
    }

    protected void ResetReliablePacket(Neta_PacketIdType Id, EProtocolMessage Message)
    {
        base.Id = Id;
        this.Message = Message;
        HeaderBytes = NetaConsts.ReliableHeaderSizeBytes;
        Retries = 0;

        Reset(NetaConsts.BufferMaxSizeBytes);
        FinalizeCalled = false;
        Flags = EPacketFlags.Reliable;

        Serialize<Neta_PacketSizeType>(0);
        Serialize(base.Id);
        Serialize(Flags, 1);
        Serialize(Message, 1);
        Serialize<Int64>(0); // Timestamp, updated before sending
    }

    public static PooledOutPacket Rent(Neta_PacketIdType Id, EProtocolMessage Message) => Rent<PooledOutPacket>(Id, Message);
    public static PooledOutPacket Rent<T>(Neta_PacketIdType Id, EProtocolMessage Message)
    {
        if (!Pool.Take(out var Packet))
        {
            Packet = new PooledOutPacket();
        }
        else
        {
            Packet.InPool = 0;
        }

        Packet.ResetPacket(Id, Message);
        return Packet;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PooledOutPacket RentReliable(Neta_PacketIdType Id, EProtocolMessage Message) => RentReliable(Id, Message);

    public static PooledOutPacket RentReliable<T>(Neta_PacketIdType Id, EProtocolMessage Message)
    {
        if (!Pool.Take(out var Packet))
        {
            Packet = new PooledOutPacket();
        }
        else
        {
            Packet.InPool = 0;
        }

        Packet.ResetReliablePacket(Id, Message);

        return Packet;
    }

    public bool IsInPool() => Interlocked.Read(ref InPool) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Return() => Return<PooledOutPacket>();
    public void Return<T>()
    {
#if NETA_DEBUG
        var Val = Interlocked.CompareExchange(ref InPool, 1, 0);
        if (Val != 0) throw new AlreadyInPoolException($"{typeof(T).Name} Attempted to return a packet that is already in the pool.");
#endif
        Pool.Add(this);
    }
}