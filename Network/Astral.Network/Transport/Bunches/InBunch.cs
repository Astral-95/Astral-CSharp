using Astral.Network.PackageMaps;
using Astral.Network.Enums;
using Astral.Network.Serialization;
using Astral.Serialization;
using System.Buffers;
using System.Collections.Concurrent;
using Astral.Exceptions;

namespace Astral.Network.Transport;

public class InBunch : NetByteReader
{
    //[ThreadStatic]
    private static ConcurrentBag<InBunch> Pool = new();
    static int INumTnstantiated = 0;
    static public int NumTnstantiated { get => INumTnstantiated; }


    protected int InPool = 0;
    internal Neta_BunchIdType Id = 0;
    public EBunchFlags Flags { get; internal set; }
    internal Neta_ChannelIndexType ChannelIndex;

#pragma warning disable CS8618
    InBunch() { Interlocked.Increment(ref INumTnstantiated); }
#pragma warning restore CS8618
    internal static InBunch Rent<T>(ConnectionPackageMap PackageMap, ByteReader InReader, Int32 NumBytes)
    {
        //if (Pool == null)
        //{
        //    Pool = new ObjectStack<InBunch>();
        //}

        if (!Pool.TryTake(out var Bunch))
        {
            Bunch = new InBunch();
            Bunch.PackageMap = PackageMap;
            Bunch.Buffer = ArrayPool<byte>.Shared.Rent(NumBytes);
            PooledObjectsTracker.OnNewPoolObject();
        }
        else
        {
            Bunch.InPool = 0;
            Bunch.PackageMap = PackageMap;

            if (Bunch.Buffer.Length < NumBytes)
            {
                ArrayPool<byte>.Shared.Return(Bunch.Buffer, clearArray: false);
                Bunch.Buffer = ArrayPool<byte>.Shared.Rent(NumBytes);
            }

            Bunch.Pos = 0;
        }

        InReader.ValidateRead(NumBytes);
        var Span = new ReadOnlySpan<byte>(InReader.GetBuffer(), InReader.Pos, NumBytes);
        Span.CopyTo(Bunch.Buffer.AsSpan(0, NumBytes));

        Bunch.Num = NumBytes;
        InReader.Pos += NumBytes;

        Bunch.Serialize(ref Bunch.Id);
        Bunch.Flags = Bunch.Serialize<EBunchFlags>();
        Bunch.ChannelIndex = Bunch.Serialize<Neta_ChannelIndexType>();
        return Bunch;
    }


    public void Return() => Return<InBunch>();


    public void Return<T>()
    {
#if NETA_DEBUG
        var Val = Interlocked.CompareExchange(ref InPool, 1, 0);
        if (Val != 0) throw new AlreadyInPoolException($"{typeof(T).Name} Attempted to return a bunch that is already in the pool.");
#endif
        PackageMap = null;
        Pool.Add(this);
    }
}