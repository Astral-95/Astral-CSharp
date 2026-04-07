using Astral.Interfaces;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Astral.Serialization;

public unsafe class ByteReader
{

    internal protected byte[] Buffer;

    Int32 PrivatePos;
    Int32 PrivateNum;

    public Int32 Pos
    {
        get => PrivatePos;
        set
        {
#if CFG_DEBUG
            if (value < 0)
                throw new InvalidOperationException($"BitReader: Pos cannot be negative (Pos={value})");
            if (PrivateNum < value)
                throw new InvalidOperationException($"BitReader: Pos cannot be greater than Num (Pos={value}, Num={PrivateNum})");
#endif
            PrivatePos = value;
        }
    }
    public int Num
    {
        get => PrivateNum;
        set
        {
#if CFG_DEBUG
            if (value < 0)
                throw new InvalidOperationException($"BitReader: Num cannot be negative (Num={value})");
            if (value > Buffer.Length)
                throw new InvalidOperationException($"BitReader: Num cannot exceed Buffer.Length (Num={value}, Buffer.Length={Buffer?.Length ?? 0})");
            if (value < PrivatePos)
                throw new InvalidOperationException($"BitReader: Num cannot be less than Pos (Num={value}, Pos={PrivatePos})");
#endif
            PrivateNum = value;
        }
    }

#pragma warning disable CS8618
    protected ByteReader()
    {
        //PrivatePos = 0;
        //PrivateNum = 0;
    }
    protected ByteReader(int NumBytes)
    {
        Buffer = new byte[NumBytes];
        Pos = 0;
        Num = 0;
    }

    public ByteReader(ByteReader Reader)
    {
        int RemainingBytes = Reader.Num - Reader.Pos;
        Buffer = new byte[RemainingBytes];
        System.Buffer.BlockCopy(Reader.Buffer, Reader.Pos, Buffer, 0, RemainingBytes);
        Pos = 0;
        Num = RemainingBytes;
    }
#pragma warning restore CS8618

    public ByteReader(byte[] Buffer, Int32 LengthBytes)
    {
        if (Buffer == null)
        {
            throw new InvalidOperationException("BitReader: Buffer cannot be null.");
        }

        if (LengthBytes > Buffer.Length)
        {
            throw new InvalidOperationException("BitReader: LengthBytes cannot be longer than Buffer.");
        }

        this.Buffer = new byte[LengthBytes];
        System.Buffer.BlockCopy(Buffer, 0, this.Buffer, 0, (int)LengthBytes);

        Pos = 0;
        Num = LengthBytes;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="BitsToRead"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public void ValidateRead(Int32 BytesToRead)
    {
        if (BytesToRead > Num - Pos || BytesToRead < 0)
        {
            throw new InvalidOperationException("BitReader: Not enough data in the buffer to read or BytesToRead is less than 0.\n" +
                $"BytesToRead: [{BytesToRead}] - Num: [{Num}] - Pos: [{Pos}] - Num-Pos(AvailableBytes): [{Num - Pos}]");
        }
    }

    #region Fields
    public TElementType Serialize<TElementType>() where TElementType : unmanaged
    {
        int SizeBytes = sizeof(TElementType);
        ValidateRead(SizeBytes);

        TElementType value;
        ReadOnlySpan<byte> SrcSpan = Buffer.AsSpan(Pos, SizeBytes);
        value = MemoryMarshal.Read<TElementType>(SrcSpan);

        Pos += SizeBytes;
        return value;
    }

    public TElementType Serialize<TElementType>(int LengthBytes) where TElementType : unmanaged
    {
        int TypeSize = Unsafe.SizeOf<TElementType>();
        if ((uint)LengthBytes < 1 || LengthBytes > TypeSize)
            throw new ArgumentOutOfRangeException(nameof(LengthBytes));

        ValidateRead(LengthBytes);

        // Prepare destination value (zero-initialized)
        TElementType Result = default;

        // Get writable byte-span over the Result value
        Span<byte> DestSpan = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref Result, 1));

        // Read incoming bytes and copy only LengthBytes into the low-order bytes
        ReadOnlySpan<byte> SrcSpan = Buffer.AsSpan(Pos, LengthBytes);
        SrcSpan.CopyTo(DestSpan); // copies into DestSpan[0..LengthBytes-1], leaves upper bytes as zero

        Pos += LengthBytes;
        return Result;
    }

    public void Serialize<TElementType>(ref TElementType Container) where TElementType : unmanaged
    {
        int SizeBytes = sizeof(TElementType);
        ValidateRead(SizeBytes);

        ReadOnlySpan<byte> SrcSpan = Buffer.AsSpan(Pos, SizeBytes);
        Container = MemoryMarshal.Read<TElementType>(SrcSpan);

        Pos += SizeBytes;
    }

    public void Serialize<TElementType>(ref TElementType Container, int LengthBytes) where TElementType : unmanaged
    {
        if (LengthBytes < 1 || LengthBytes > sizeof(TElementType))
            throw new ArgumentOutOfRangeException(nameof(LengthBytes));

        ValidateRead(LengthBytes);

        ReadOnlySpan<byte> SrcSpan = Buffer.AsSpan(Pos, LengthBytes);
        Container = MemoryMarshal.Read<TElementType>(SrcSpan);

        Pos += LengthBytes;
    }

    public void Serialize(ref string Container)
    {
        ValidateRead(4);

        int LengthBytes = 0;
        Serialize(ref LengthBytes);

        if (LengthBytes < 1)
        {
            Container = "";
            return;
        }

        ValidateRead(LengthBytes);

        Container = Encoding.UTF8.GetString(Buffer, Pos, LengthBytes);
        Pos += LengthBytes;
    }

    public string SerializeString()
    {
        ValidateRead(4);

        int LengthBytes = 0;
        Serialize(ref LengthBytes);

        if (LengthBytes < 1) return "";

        ValidateRead(LengthBytes);

        var Str = Encoding.UTF8.GetString(Buffer, Pos, LengthBytes);
        Pos += LengthBytes;
        return Str;
    }
    #endregion Fields


    #region Arrays
    void PrivateSerializeArray<TElementType>(ref TElementType[] Array, int NumElements, int LengthBytes) where TElementType : unmanaged
    {
        Array = new TElementType[NumElements];

        fixed (void* DestBuffer = Array)
        fixed (byte* SrcBuffer = this.Buffer)
        {
            Unsafe.CopyBlock(DestBuffer, SrcBuffer + Pos, (uint)LengthBytes);
        }

        Pos += LengthBytes;
    }
    TElementType[] PrivateSerializeArray<TElementType>(int NumElements, int LengthBytes) where TElementType : unmanaged
    {
        TElementType[] Arr = new TElementType[NumElements];

        fixed (void* DstBuffer = Arr)
        fixed (byte* SrcBuffer = this.Buffer)
        {
            Unsafe.CopyBlock(DstBuffer, SrcBuffer + Pos, (uint)LengthBytes);
        }

        Pos += LengthBytes;
        return Arr;
    }

    public TElementType[] SerializeArray<TElementType>() where TElementType : unmanaged
    {
        int Count = 0;
        Serialize(ref Count);
        if (Count < 1) return Array.Empty<TElementType>();

        int LengthBytes = Count * sizeof(TElementType);
        ValidateRead(LengthBytes);

        return PrivateSerializeArray<TElementType>(Count, LengthBytes);
    }

    public void SerializeArray<TElementType>(ref TElementType[] Array) where TElementType : unmanaged
    {
        int Count = 0;
        Serialize(ref Count);
        if (Count < 1) return;

        int LengthBytes = Count * sizeof(TElementType);
        ValidateRead(LengthBytes);

        PrivateSerializeArray(ref Array, Count, LengthBytes);
    }
    public void SerializeArray(ref string[] Array)
    {
        int Count = 0;
        Serialize(ref Count);
        if (Count < 1) return;

        Array = new string[Count];
        for (int i = 0; i < Count; i++)
        {
            string Str = "";
            Serialize(ref Str);
            Array[i] = Str;
        }
    }

    public string[] SerializeStringArray()
    {
        int Count = 0;
        Serialize(ref Count);
        if (Count < 1) return System.Array.Empty<string>();

        string[] Array = new string[Count];
        for (int i = 0; i < Count; i++)
        {
            string Str = "";
            Serialize(ref Str);
            Array[i] = Str;
        }
        return Array;
    }
    #endregion Arrays


    #region Lists
    public void Serialize(List<string> Container)
    {
        Container.Clear();

        int Count = 0;
        Serialize(ref Count);

        if (Count < 1) return;

        for (int i = 0; i < Count; i++)
        {
            string Str = "";
            Serialize(ref Str);
            Container.Add(Str);
        }
    }

    public void Serialize<TElementType>(List<TElementType> Container) where TElementType : unmanaged
    {
        Container.Clear();

        int Count = 0;
        Serialize(ref Count);
        if (Count < 1) return;

        int ElementSize = sizeof(TElementType);
        int BytesToRead = Count * ElementSize;

        ValidateRead(BytesToRead);

        // Prepare list
        Container.Clear();
        Container.Capacity = Count;
        for (int i = 0; i < Count; i++) Container.Add(default);

        Span<TElementType> ElementSpan = CollectionsMarshal.AsSpan(Container);
        int ByteOffset = Pos;

        fixed (byte* SrcPtr = &Buffer[ByteOffset])
        fixed (TElementType* DstPtr = &ElementSpan[0])
        {
            System.Buffer.MemoryCopy(SrcPtr, DstPtr, BytesToRead, BytesToRead);
        }

        Pos += BytesToRead;
    }
    #endregion

    public virtual IObject? SerializeObject() { return null; }

    public byte[] GetBuffer()
    {
        return Buffer;
    }
}