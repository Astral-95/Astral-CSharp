using Astral.Exceptions;
using System.Collections.Concurrent;

namespace Astral.Serialization;

public sealed class PooledByteReader : ByteReader
{
    int InPool = 0;
    static readonly ConcurrentBag<PooledByteReader> Pool = new ConcurrentBag<PooledByteReader>();


#pragma warning disable CS8618
    PooledByteReader() { }
    PooledByteReader(byte[] Buffer, Int32 LengthBytes) : base(Buffer, LengthBytes) { }
#pragma warning restore CS8618

    public static PooledByteReader Rent(byte[] Buffer, Int32 LengthBytes)
    {
        if (LengthBytes > Buffer.Length)
        {
            throw new InvalidOperationException("BitReader: [(LengthBits + 7) / 8] cannot be longer than Buffer.");
        }

        if (!Pool.TryTake(out var Reader))
        {
            Reader = new PooledByteReader(Buffer, LengthBytes);
        }
        else
        {
            Reader.InPool = 0;

            if (LengthBytes > Reader.Buffer.Length)
            {
                Reader.Buffer = new byte[LengthBytes];
                System.Buffer.BlockCopy(Buffer, 0, Reader.Buffer, 0, (int)LengthBytes);
            }

            Reader.Pos = 0;
            Reader.Num = LengthBytes;
        }

#if CFG_DEBUG
        PooledObjectsTracker.Register(Reader);
#endif
        return Reader;
    }

    public void Return()
    {
#if CFG_DEBUG
        var Val = Interlocked.CompareExchange(ref InPool, 1, 0);
        if (Val != 0) throw new AlreadyInPoolException("Attempted to return a reader that is already in the pool.");
        PooledObjectsTracker.Unregister(this);
#endif
        Pool.Add(this);
    }
}