using Astral.Containers;
using Astral.Exceptions;
using Astral.Network.Enums;
using Astral.Serialization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Astral.Network.Transport;

public unsafe class PooledInPacket : InPacket
{
#if LINUX
    [ThreadStatic]
    private static ObjectStack<PooledInPacket> Pool;
#else
    private static readonly ConcurrentFastQueue<PooledInPacket> Pool = new ConcurrentFastQueue<PooledInPacket>();
#endif



    static int INumTnstantiated = 0;
    static public int NumTnstantiated { get => INumTnstantiated; }
    protected long InPool = 0;

    internal PooledInPacket(int NumBytes = NetaConsts.BufferMaxSizeBytes)
    {
        if (NumBytes < NetaConsts.BufferMaxSizeBytes)
        {
            NumBytes = NetaConsts.BufferMaxSizeBytes;
        }

        Resize(NumBytes);
        Interlocked.Increment(ref INumTnstantiated);
    }

    public static void InitForWorker(int Preload)
    {
#if LINUX
        if (Pool == null) Pool = new();
#endif

        for (int i = 0; i < Preload; i++)
        {
#if LINUX
            Pool.Add(new PooledInPacket()); 
#else
            Pool.Enqueue(new PooledInPacket());
#endif
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
#if LINUX
        if (Pool == null) Pool = new();
#endif
#if LINUX
        if (Pool.Take(out var Packet))
        {
            Packet.InPool = 0;
        }
#else
        if (Pool.TryDequeue(out var Packet))
        {
            Packet.InPool = 0;
        }
#endif
        else
        {
            Packet = new PooledInPacket();
        }

        byte[] ManagedBuffer = PacketOut.GetBuffer();
        int DataLength = PacketOut.Pos;

        fixed (byte* SrcPtr = ManagedBuffer)
        {
            // Copy from the pinned managed buffer to our permanently pinned unmanaged Buffer
            // Unsafe.CopyBlock is faster than System.Buffer.BlockCopy for pointer destinations
            Unsafe.CopyBlock(Packet.Buffer, SrcPtr, (uint)DataLength);
        }

        Packet.Pos = 0;
        Packet.Num = PacketOut.Pos;
        return Packet;
    }

    public static PooledInPacket Rent<T>()
    {
#if LINUX
       if (Pool == null) Pool = new();
#endif

#if LINUX
        if (!Pool.Take(out var Packet))
        {
            Packet = new PooledInPacket();
        }
#else
        if (!Pool.TryDequeue(out var Packet))
        {
            Packet = new PooledInPacket();
        }
#endif
        else
        {
            Packet.InPool = 0;
            Packet.ResetPacket();
        }
        return Packet;
    }


    internal static PooledInPacket Rent<T>(ByteWriter Writer)
    {
#if LINUX
        if (Pool == null)
        {
            Pool = new();
        } 
#endif

#if LINUX
        if (!Pool.Take(out var Packet))
        {
            Packet = new PooledInPacket(Writer.Pos);
        }
#else
        if (!Pool.TryDequeue(out var Packet))
        {
            Packet = new PooledInPacket(Writer.Pos);
        }
#endif
        else
        {
            Packet.InPool = 0;
            Packet.Pos = 0;
            Packet.Num = 0;

            Packet.Resize(Writer.Pos);
        }

        byte[] ManagedSource = Writer.GetBuffer();

        fixed (byte* SrcPtr = ManagedSource)
        {
            // Copy directly from pinned managed memory to our unmanaged pointer
            // Using Unsafe.CopyBlock because System.Buffer.BlockCopy won't accept a byte*
            Unsafe.CopyBlock(Packet.Buffer, SrcPtr, (uint)Writer.Pos);
        }

        Packet.Num = Writer.Pos;

        return Packet;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Return() => Return<PooledInPacket>();


    public void Return<T>()
    {
        var Val = Interlocked.CompareExchange(ref InPool, 1, 0);
        if (Val != 0) throw new AlreadyInPoolException($"{typeof(T).Name} Attempted to return a packet that is already in the pool.");
#if LINUX
        Pool.Add(this); 
#else
        Pool.Enqueue(this);
#endif
    }

    public void TryReturn()
    {
        if (Interlocked.CompareExchange(ref InPool, 1, 0) != 0) return;
#if LINUX
        Pool.Add(this); 
#else
        Pool.Enqueue(this);
#endif
    }
}