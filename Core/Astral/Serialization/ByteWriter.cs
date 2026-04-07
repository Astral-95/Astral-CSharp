using Astral.Interfaces;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Astral.Serialization;

public unsafe class ByteWriter
{
    internal protected byte[] Buffer;

    Int32 PrivatePos;
    Int32 PrivateNum;

    public Int32 Pos
    {
        get => PrivatePos;
        internal protected set
        {
#if CFG_DEBUG
            if (value < 0)
                throw new InvalidOperationException($"ByteWriter: Pos cannot be negative (Pos={value})");
            if (PrivateNum < value)
                throw new InvalidOperationException($"ByteWriter: Pos cannot be greater than Num (Pos={value}, Num={PrivateNum})");
#endif
            PrivatePos = value;
        }
    }
    public int Num
    {
        get => PrivateNum;
        internal protected set
        {
#if CFG_DEBUG
            if (value < 0)
                throw new InvalidOperationException($"ByteWriter: Num cannot be negative (Num={value})");
            if (value > Buffer.Length)
                throw new InvalidOperationException($"ByteWriter: Num cannot exceed Buffer.Length (Num={value}, Buffer.Length={Buffer?.Length ?? 0})");
            if (value < PrivatePos)
                throw new InvalidOperationException($"ByteWriter: Num cannot be less than Pos (Num={value}, Pos={PrivatePos})");
#endif
            PrivateNum = value;
        }
    }

#pragma warning disable CS8618
    protected ByteWriter() { }
    public ByteWriter(Int32 InitialSizeBytes = 128)
    {
        if (InitialSizeBytes < 0) throw new Exception("BitWriter: Buffer size cannot be negative.");

        Buffer = new byte[InitialSizeBytes];
        Num = InitialSizeBytes;
        Pos = 0;
    }

    public virtual void Reset(Int32 MinBytes = 64)
    {
        if (MinBytes < 0) throw new Exception("BitWriter: Buffer size cannot be negative.");

        if (Num < MinBytes)
        {
            Buffer = new byte[MinBytes];
            Num = MinBytes;
        }

        Pos = 0;
    }



    public void SetPos(Int32 Pos) { this.Pos = Pos; }
    public void SetNum(Int32 Num) { this.Num = Num; }

    private void EnsureCapacity(Int32 BytesToAdd)
    {
        int TotalBytes = Pos + BytesToAdd;

        if (TotalBytes > Buffer.Length) Grow(TotalBytes);
    }

    private void Grow(Int32 BytesToAdd)
    {
        // calculate next power-of-two size (same as before)
        int NewSize = 1 << (32 - BitOperations.LeadingZeroCount((uint)(BytesToAdd - 1)));

        byte[] NewBuffer = new byte[NewSize];

        // Copy existing data into new buffer
        if (Pos > 0) Buffer.AsSpan(0, Pos).CopyTo(NewBuffer);

        // Replace buffer and update size
        Buffer = NewBuffer;
        Num = NewSize;
    }

    #region Primitives
    public void Serialize(string Container)
    {
        if (Container == null) throw new ArgumentNullException(nameof(Container));

        byte[] Bytes = System.Text.Encoding.UTF8.GetBytes(Container);
        int Count = Bytes.Length;

        // Write string length first
        Serialize(Count);

        if (Count == 0) return;

        EnsureCapacity(Count);

        // Fast bulk copy
        Bytes.AsSpan().CopyTo(Buffer.AsSpan(Pos, Count));
        Pos += Count;
    }
    public void Serialize<TType>(TType Container) where TType : unmanaged
    {
        int SizeBytes = sizeof(TType);
        EnsureCapacity(SizeBytes);

        Span<byte> DestSpan = Buffer.AsSpan(Pos, SizeBytes);
        MemoryMarshal.Write(DestSpan, in Container);

        Pos += SizeBytes;
    }

    public void Serialize<TType>(TType Container, int LengthBytes) where TType : unmanaged
    {
        int TypeSize = Unsafe.SizeOf<TType>();
        if ((uint)LengthBytes < 1 || LengthBytes > TypeSize)
            throw new ArgumentOutOfRangeException(nameof(LengthBytes));

        EnsureCapacity(LengthBytes);

        // Get a ReadOnlySpan<byte> view over the TType value (no allocation, no copy yet)
        ReadOnlySpan<byte> SrcSpan = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref Container, 1));

        // Create a ref to destination start (avoids extra Span allocations/copies)
        ref byte DestRef = ref MemoryMarshal.GetReference(Buffer.AsSpan(Pos, LengthBytes));

        // Copy only the requested bytes (LSB-first slice)
        ReadOnlySpan<byte> Slice = SrcSpan.Slice(0, LengthBytes);
        Slice.CopyTo(MemoryMarshal.CreateSpan(ref DestRef, LengthBytes));

        Pos += LengthBytes;
    }
    #endregion Primitives

    #region Array
    public void Serialize<TElementType>(TElementType[] Array) where TElementType : unmanaged
    {
        ArgumentNullException.ThrowIfNull(Array);

        int Count = Array.Length;
        Serialize(Count); // write length prefix
        if (Count == 0) return;

        int TypeSize = sizeof(TElementType);
        int LengthBytes = Count * TypeSize;

        EnsureCapacity(LengthBytes); // ensure bit capacity

        // Fast bulk copy using MemoryMarshal
        ReadOnlySpan<byte> SrcBytes = MemoryMarshal.AsBytes(Array.AsSpan());
        SrcBytes.CopyTo(Buffer.AsSpan(Pos, LengthBytes));

        Pos += LengthBytes;
    }
    public void Serialize(string[] Array)
    {
        ArgumentNullException.ThrowIfNull(Array);

        int Count = Array.Length;
        Serialize(Count);
        if (Count == 0) return;

        for (int i = 0; i < Count; i++)
            Serialize(Array[i]); // each string is already byte-aligned
    }
    public void Serialize<TElementType>(TElementType[] Array, int StartByte, int LengthBytes) where TElementType : unmanaged
    {
        ArgumentNullException.ThrowIfNull(Array);

        if (StartByte < 0 || LengthBytes < 1 || StartByte + LengthBytes > Array.Length * sizeof(TElementType))
            throw new ArgumentOutOfRangeException($"StartByte < 0 || LengthBytes < 1 || StartByte + LengthBytes > Array.Length * sizeof(TElementType))\nStartByte: [{StartByte}] LengthBytes: [{LengthBytes}] StartByte + LengthBytes: [{StartByte + LengthBytes}] Array.Length * sizeof({typeof(TElementType).Name}): [{Array.Length * sizeof(TElementType)}]");

        EnsureCapacity(LengthBytes);

        fixed (TElementType* SrcPtr = Array)
        fixed (byte* DstPtr = Buffer)
        {
            Unsafe.CopyBlock(DstPtr + Pos, (byte*)SrcPtr + StartByte, (uint)LengthBytes);
        }

        Pos += LengthBytes;
    }
    #endregion Array

    #region Lists
    public void Serialize(List<string> Container)
    {
        ArgumentNullException.ThrowIfNull(Container);

        int Count = Container.Count;
        Serialize(Count);
        if (Count == 0) return;

        for (int i = 0; i < Count; i++) Serialize(Container[i]);
    }

    public void Serialize<TElementType>(List<TElementType> Container) where TElementType : unmanaged
    {
        if (Container == null) throw new ArgumentNullException(nameof(Container));

        int Count = Container.Count;
        Serialize(Count);
        if (Count == 0) return;

        int ElementSize = sizeof(TElementType);
        int BytesToWrite = Count * ElementSize;

        EnsureCapacity(BytesToWrite);

        Span<TElementType> ElementSpan = CollectionsMarshal.AsSpan(Container);
        MemoryMarshal.AsBytes(ElementSpan).CopyTo(Buffer.AsSpan(Pos, BytesToWrite));

        Pos += BytesToWrite;
    }

    public void Serialize<TElementType>(Span<TElementType> Container) where TElementType : unmanaged
    {
        int Count = Container.Length;
        Serialize(Count);
        if (Count == 0) return;

        int ElementSize = sizeof(TElementType);
        int BytesToWrite = Count * ElementSize;

        EnsureCapacity(BytesToWrite);

        MemoryMarshal.AsBytes(Container).CopyTo(Buffer.AsSpan(Pos, BytesToWrite));

        Pos += BytesToWrite;
    }

    public void Serialize<TElementType>(Span<TElementType> Container, int StartByte, int LengthBytes) where TElementType : unmanaged
    {
        if (StartByte < 0 || LengthBytes < 1 || StartByte + LengthBytes > Container.Length * sizeof(TElementType))
            throw new ArgumentOutOfRangeException($"StartByte < 0 || LengthBytes < 1 || StartByte + LengthBytes > Array.Length * sizeof(TElementType))\nStartByte: [{StartByte}] LengthBytes: [{LengthBytes}] StartByte + LengthBytes: [{StartByte + LengthBytes}] Array.Length * sizeof({typeof(TElementType).Name}): [{Container.Length * sizeof(TElementType)}]");

        EnsureCapacity(LengthBytes);

        fixed (TElementType* SrcPtr = Container)
        fixed (byte* DstPtr = Buffer)
        {
            Unsafe.CopyBlock(DstPtr + Pos, (byte*)SrcPtr + StartByte, (uint)LengthBytes);
        }

        Pos += LengthBytes;
    }
    #endregion


    public void Serialize(ByteWriter Writer)
    {
        Serialize(Writer.GetBuffer(), 0, Writer.Pos);
    }

    public void Serialize(ByteReader Reader)
    {
        Serialize(Reader.GetBuffer(), Reader.Pos, Reader.Num - Reader.Pos);
    }


    public virtual void SerializeObject(IObject? Obj) { }

    public byte[] GetBuffer()
    {
        return Buffer;
    }
}