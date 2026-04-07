using Astral.Containers;
using Astral.Network.Enums;
using Astral.Serialization;
using Astral.Exceptions;

namespace Astral.Network.Transport;

public class PooledInPacket : InPacket
{
    private static readonly ConcurrentStore<PooledInPacket> Pool = new ConcurrentStore<PooledInPacket>();


    static int INumTnstantiated = 0;
    static public int NumTnstantiated { get => INumTnstantiated; }
    protected long InPool = 0;

    internal PooledInPacket(int NumBytes = NetaConsts.BufferMaxSizeBytes)
    {
        if (NumBytes < NetaConsts.BufferMaxSizeBytes)
        {
            NumBytes = NetaConsts.BufferMaxSizeBytes;
        }

        Buffer = new byte[NumBytes];
        Pos = 0;
        Num = 0;
        Interlocked.Increment(ref INumTnstantiated);
    }

    public static void PrePopulate(int Num)
    {
        for (int i = 0; i < Num; i++)
        {
            Pool.Add(new PooledInPacket());
        }
    }

    public static long GetPoolSize() { return Pool.Count; }

    internal void Init()
    {
        //var NumBytes = MemoryMarshal.Read<Neta_PacketSizeType>(Buffer);
        //NetGuard.DebugAssert(NumBytes > 0);
        //Num = NumBytes;
        Pos = NetaConsts.PacketNumBytesSizeBytes;

        Serialize(ref Id);
        Flags = Serialize<EPacketFlags>();
        Serialize(ref Message, 1);
    }

    internal void ResetPacket()
    {
        Pos = 0;
        Num = 0;
    }

    internal static PooledInPacket Rent<T>(OutPacket PacketOut)
    {
        if (Pool.Take(out var Packet))
        {
            Packet.InPool = 0;
        }
        else
        {
            Packet = new PooledInPacket();
        }

        System.Buffer.BlockCopy(PacketOut.GetBuffer(), 0, Packet.Buffer, 0, PacketOut.Pos);
        Packet.Pos = 0;
        Packet.Num = PacketOut.Pos;
        return Packet;
    }

    public static PooledInPacket Rent<T>()
    {
        if (!Pool.Take(out var Packet))
        {
            Packet = new PooledInPacket();
        }
        else
        {
            Packet.InPool = 0;
            Packet.ResetPacket();
        }
        return Packet;
    }


    internal static PooledInPacket Rent(ByteWriter Writer)
    {
        if (!Pool.Take(out var Packet))
        {
            Packet = new PooledInPacket(Writer.Pos);
        }
        else
        {
            Packet.InPool = 0;
            Packet.Pos = 0;

            if (Packet.Buffer.Length < Writer.Pos)
            {
                Packet.Buffer = new byte[Writer.Pos];
            }
        }

        var Reader = Packet;

        //new ReadOnlySpan<byte>(Writer.Buffer, 0, Writer.Pos).CopyTo(Reader.Buffer.AsSpan(0, Writer.Pos));
        System.Buffer.BlockCopy(Writer.GetBuffer(), 0, Reader.Buffer, 0, Writer.Pos);

        Reader.Num = Writer.Pos;
        return Packet;
    }

    public void Return() => Return<PooledInPacket>();


    public void Return<T>()
    {
        var Val = Interlocked.CompareExchange(ref InPool, 1, 0);
        if (Val != 0) throw new AlreadyInPoolException($"{typeof(T).Name} Attempted to return a packet that is already in the pool.");
        Pool.Add(this);
    }

    public void TryReturn()
    {
        if (Interlocked.CompareExchange(ref InPool, 1, 0) != 0) return;
        Pool.Add(this);
    }
}