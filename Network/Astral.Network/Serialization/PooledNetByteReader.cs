using Astral.Containers;
using Astral.Network.PackageMaps;
using Astral.Interfaces;
using Astral.Serialization;
using System.Buffers;

namespace Astral.Network.Serialization;

public sealed class PooledNetByteReader : NetByteReader
{
    int InPool = 0;
    private static readonly ConcurrentFastQueue<PooledNetByteReader> Pool = new ConcurrentFastQueue<PooledNetByteReader>();

    internal PooledNetByteReader() { }

    internal PooledNetByteReader(NetByteReader Reader, Int32 LengthBytes)
    {
        Reader.ValidateRead(LengthBytes);
        Buffer = ArrayPool<byte>.Shared.Rent(LengthBytes);

        System.Buffer.BlockCopy(Reader.GetBuffer(), Reader.Pos, Buffer, 0, LengthBytes);
        Num = LengthBytes;

        Reader.Pos += LengthBytes;
    }
    internal PooledNetByteReader(ConnectionPackageMap PackageMap, NetByteReader Reader, Int32 LengthBytes)
    {
        this.PackageMap = PackageMap;
        Reader.ValidateRead(LengthBytes);
        Buffer = ArrayPool<byte>.Shared.Rent(LengthBytes);

        System.Buffer.BlockCopy(Reader.GetBuffer(), Reader.Pos, Buffer, 0, LengthBytes);
        Num = LengthBytes;

        Reader.Pos += LengthBytes;
    }

    internal PooledNetByteReader(ByteWriter Writer)
    {
        var LengthBytes = (Writer.Pos + 7) / 8;

        Buffer = ArrayPool<byte>.Shared.Rent(LengthBytes);
        System.Buffer.BlockCopy(Writer.GetBuffer(), 0, Buffer, 0, LengthBytes);

        Num = Writer.Pos;
    }
    public static long GetPoolSize() { return Pool.Count; }
    public static PooledNetByteReader Rent(int NumBytes = NetaConsts.BufferMaxSizeBytes)
    {
        if (!Pool.TryDequeue(out var Reader))
        {
            Reader = new PooledNetByteReader();
            Reader.Buffer = ArrayPool<byte>.Shared.Rent(NumBytes);
            PooledObjectsTracker.OnNewPoolObject();
        }
        else
        {
            Reader.InPool = 0;
            Reader.PrivateReset(NumBytes);
            Reader.Pos = 0;
            Reader.Num = 0;
        }
#if NETA_DEBUG
        PooledObjectsTracker.Register(Reader);
#endif
        return Reader;
    }

    public static PooledNetByteReader Rent(ByteWriter Writer)
    {
        if (!Pool.TryDequeue(out var Reader))
        {
            Reader = new PooledNetByteReader(Writer);
            Reader.Buffer = ArrayPool<byte>.Shared.Rent(NetaConsts.BufferMaxSizeBytes);
            PooledObjectsTracker.OnNewPoolObject();
        }
        else
        {
            Int32 RequiredBytes = (Writer.Pos + 7) / 8;

            if (Reader.Buffer.Length < RequiredBytes)
            {
                ArrayPool<byte>.Shared.Return(Reader.Buffer);
                Reader.Buffer = ArrayPool<byte>.Shared.Rent(RequiredBytes);
            }

            System.Buffer.BlockCopy(Writer.GetBuffer(), 0, Reader.Buffer, 0, RequiredBytes);

            Reader.InPool = 0;
            Reader.Pos = 0;
            Reader.Num = Writer.Pos;
        }
#if NETA_DEBUG
        PooledObjectsTracker.Register(Reader);
#endif
        return Reader;
    }



    internal static PooledNetByteReader Rent(NetByteReader InReader, Int32 LengthBits)
    {
        InReader.ValidateRead(LengthBits);

        if (!Pool.TryDequeue(out var Reader))
        {
            Reader = new PooledNetByteReader(InReader, LengthBits);
            PooledObjectsTracker.OnNewPoolObject();
        }
        else
        {
            Reader.InPool = 0;
            Reader.InternalReset(InReader, LengthBits);
        }
#if NETA_DEBUG
        PooledObjectsTracker.Register(Reader);
#endif
        return Reader;
    }

    internal static PooledNetByteReader Rent(ConnectionPackageMap PackageMap, NetByteReader InReader, Int32 LengthBits)
    {
        InReader.ValidateRead(LengthBits);

        if (!Pool.TryDequeue(out var Reader))
        {
            Reader = new PooledNetByteReader(PackageMap, InReader, LengthBits);
            PooledObjectsTracker.OnNewPoolObject();
        }
        else
        {
            Reader.InPool = 0;
            Reader.PrivateReset(PackageMap, InReader, LengthBits);
        }
#if NETA_DEBUG
        PooledObjectsTracker.Register(Reader);
#endif
        return Reader;
    }


    void PrivateReset(int NumBytes = NetaConsts.BufferMaxSizeBytes)
    {
        if (Buffer.Length < NumBytes)
        {
            ArrayPool<byte>.Shared.Return(Buffer);
            Buffer = ArrayPool<byte>.Shared.Rent(NumBytes);
        }
    }

    void PrivateReset(ConnectionPackageMap PackageMap, NetByteReader Reader, int LengthBytes)
    {
        Reader.ValidateRead(LengthBytes);
        this.PackageMap = PackageMap;

        if (Buffer.Length < LengthBytes)
        {
            ArrayPool<byte>.Shared.Return(Buffer);
            Buffer = ArrayPool<byte>.Shared.Rent(LengthBytes);
        }

        Pos = 0;
        System.Buffer.BlockCopy(Reader.GetBuffer(), Reader.Pos, Buffer, 0, LengthBytes);
        Num = LengthBytes;

        Reader.Pos += LengthBytes;
    }

    internal void InternalReset(NetByteReader Reader, int LengthBytes)
    {
        Reader.ValidateRead(LengthBytes);
        PrivateReset(LengthBytes);
        Pos = 0;

        System.Buffer.BlockCopy(Reader.GetBuffer(), Reader.Pos, Buffer, 0, LengthBytes);
        Num = LengthBytes;

        Reader.Pos += LengthBytes;
    }

    public void Return()
    {
#if NETA_DEBUG
        var Val = Interlocked.CompareExchange(ref InPool, 1, 0);
        if (Val != 0) throw new InvalidOperationException();
        PooledObjectsTracker.Unregister(this);
#endif
        PackageMap = null;
        Pool.Enqueue(this);
    }


    public override IObject? SerializeObject()
    {
        return PackageMap!.SerializeObject(this);
    }
}