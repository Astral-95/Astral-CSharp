using Astral.Exceptions;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Astral.Serialization;

public sealed class PooledUnmanagedByteReader : UnmanagedByteReader
{
    int InPool = 0;
    static readonly ConcurrentBag<PooledUnmanagedByteReader > Pool = new ConcurrentBag<PooledUnmanagedByteReader >();


#pragma warning disable CS8618
    PooledUnmanagedByteReader () { }
    PooledUnmanagedByteReader (byte[] Buffer, Int32 LengthBytes) : base(Buffer, LengthBytes) { }
    PooledUnmanagedByteReader (UnmanagedByteReader Reader) : base(Reader) { }
#pragma warning restore CS8618

    public unsafe static PooledUnmanagedByteReader Rent(byte[] ManagedBuffer, Int32 LengthBytes)
    {
        if (LengthBytes > ManagedBuffer.Length)
        {
            throw new InvalidOperationException("BitReader: [(LengthBits + 7) / 8] cannot be longer than Buffer.");
        }

        if (!Pool.TryTake(out var Reader))
        {
            Reader = new PooledUnmanagedByteReader (ManagedBuffer, LengthBytes);
        }
        else
        {
            Reader.InPool = 0;

            fixed (byte* pManaged = ManagedBuffer)
            {
                NativeMemory.Copy(pManaged, Reader.Buffer, (nuint)LengthBytes);
            }

            Reader.Pos = 0;
            Reader.Num = LengthBytes;
        }

        return Reader;
    }

    public unsafe static PooledUnmanagedByteReader Rent(UnmanagedByteReader InReader)
    {
        if (!Pool.TryTake(out var Reader))
        {
            Reader = new PooledUnmanagedByteReader(InReader);
        }
        else
        {
            Reader.InPool = 0;

            var Remaining = InReader.Num - InReader.Pos;
            Reader.Resize(Remaining);
            NativeMemory.Copy(InReader.Buffer + InReader.Pos, Reader.Buffer, (nuint)Remaining); 

            Reader.Pos = 0;
            Reader.Num = Remaining;
        }

        return Reader;
    }

    public void Return()
    {
#if CFG_DEBUG
        var Val = Interlocked.CompareExchange(ref InPool, 1, 0);
        if (Val != 0) throw new AlreadyInPoolException("Attempted to return a reader that is already in the pool.");
#endif
        Pool.Add(this);
    }
}