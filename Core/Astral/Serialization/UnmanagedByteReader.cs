using Astral.Interfaces;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Astral.Serialization;

public unsafe class UnmanagedByteReader : MemoryManager<byte>
{
    internal protected byte* Buffer;

    public Int32 Length { get; private set; } // The total allocated size
    Int32 PrivatePos;
    Int32 PrivateNum; // The actual data length

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
            if (value > Length)
                throw new InvalidOperationException($"BitReader: Num cannot exceed Buffer.Length (Num={value}, Buffer.Length={Length})");
            if (value < PrivatePos)
                throw new InvalidOperationException($"BitReader: Num cannot be less than Pos (Num={value}, Pos={PrivatePos})");
#endif
            PrivateNum = value;
        }
    }

#pragma warning disable CS8618
    protected UnmanagedByteReader()
    {
        Buffer = null;
        //PrivatePos = 0;
        //PrivateNum = 0;
    }
    protected UnmanagedByteReader(int NumBytes)
    {
        Buffer = (byte*)NativeMemory.AlignedAlloc((nuint)NumBytes, 64);
        Length = NumBytes;
        Pos = 0;
        Num = 0;
    }

    public UnmanagedByteReader(UnmanagedByteReader Reader)
    {
        int RemainingBytes = Reader.Num - Reader.Pos;

        // Create a new independent buffer
        Buffer = (byte*)NativeMemory.AlignedAlloc((nuint)RemainingBytes, 64);
        Length = RemainingBytes;

        // Use NativeMemory.Copy for pointer-to-pointer transfer
        NativeMemory.Copy(Reader.Buffer + Reader.Pos, Buffer, (nuint)RemainingBytes);

        Pos = 0;
        Num = RemainingBytes;
    }
#pragma warning restore CS8618

    public UnmanagedByteReader(byte[] ManagedBuffer, int LengthBytes)
    {
        if (ManagedBuffer == null) throw new ArgumentNullException(nameof(ManagedBuffer));
        if (LengthBytes > ManagedBuffer.Length) throw new ArgumentOutOfRangeException(nameof(LengthBytes));

        Buffer = (byte*)NativeMemory.AlignedAlloc((nuint)LengthBytes, 64);
        Length = LengthBytes;

        // Pin the managed buffer temporarily to copy into our unmanaged pointer
        fixed (byte* pManaged = ManagedBuffer)
        {
            NativeMemory.Copy(pManaged, Buffer, (nuint)LengthBytes);
        }

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

        // Using Unsafe.ReadUnaligned to read directly from the pointer
        TElementType Value = Unsafe.ReadUnaligned<TElementType>(Buffer + PrivatePos);

        PrivatePos += SizeBytes;
        return Value;
    }

    public TElementType Serialize<TElementType>(int LengthBytes) where TElementType : unmanaged
    {
        int TypeSize = sizeof(TElementType);
        if ((uint)LengthBytes < 1 || LengthBytes > TypeSize)
            throw new ArgumentOutOfRangeException(nameof(LengthBytes));

        ValidateRead(LengthBytes);

        TElementType Result = default;

        // Copy raw bytes from Buffer into the address of Result
        System.Runtime.CompilerServices.Unsafe.CopyBlock(
            Unsafe.AsPointer(ref Result),
            Buffer + PrivatePos,
            (uint)LengthBytes);

        PrivatePos += LengthBytes;
        return Result;
    }

    public void Serialize<TElementType>(ref TElementType Container) where TElementType : unmanaged
    {
        int SizeBytes = sizeof(TElementType);
        ValidateRead(SizeBytes);

        Container = Unsafe.ReadUnaligned<TElementType>(Buffer + PrivatePos);

        PrivatePos += SizeBytes;
    }

    public void Serialize<TElementType>(ref TElementType Container, int LengthBytes) where TElementType : unmanaged
    {
        if (LengthBytes < 1 || LengthBytes > sizeof(TElementType))
            throw new ArgumentOutOfRangeException(nameof(LengthBytes));

        ValidateRead(LengthBytes);

        // Initialize container to zero first if LengthBytes < sizeof(TElementType)
        Container = default;

        System.Runtime.CompilerServices.Unsafe.CopyBlock(
            Unsafe.AsPointer(ref Container),
            Buffer + PrivatePos,
            (uint)LengthBytes);

        PrivatePos += LengthBytes;
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

        // Using the pointer-based overload of UTF8.GetString
        Container = Encoding.UTF8.GetString(Buffer + PrivatePos, LengthBytes);
        PrivatePos += LengthBytes;
    }

    public string SerializeString()
    {
        ValidateRead(4);

        int LengthBytes = 0;
        Serialize(ref LengthBytes);

        if (LengthBytes < 1) return "";

        ValidateRead(LengthBytes);

        string Str = Encoding.UTF8.GetString(Buffer + PrivatePos, LengthBytes);
        PrivatePos += LengthBytes;
        return Str;
    }
    #endregion Fields


    #region Arrays
    private void PrivateSerializeArray<TElementType>(ref TElementType[] Array, int NumElements, int LengthBytes) where TElementType : unmanaged
    {
        Array = new TElementType[NumElements];

        // Pin the managed destination array to copy into it
        fixed (void* DestBuffer = Array)
        {
            // Buffer is already a pointer, no pinning required
            Unsafe.CopyBlock(DestBuffer, Buffer + PrivatePos, (uint)LengthBytes);
        }

        PrivatePos += LengthBytes;
    }
    private TElementType[] PrivateSerializeArray<TElementType>(int NumElements, int LengthBytes) where TElementType : unmanaged
    {
        TElementType[] Arr = new TElementType[NumElements];

        fixed (void* DstBuffer = Arr)
        {
            Unsafe.CopyBlock(DstBuffer, Buffer + PrivatePos, (uint)LengthBytes);
        }

        PrivatePos += LengthBytes;
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
    public void SerializeArray(ref string[] Arr)
    {
        int Count = 0;
        Serialize(ref Count);
        if (Count < 1)
        {
            Arr = Array.Empty<string>();
            return;
        }

        Arr = new string[Count];
        for (int i = 0; i < Count; i++)
        {
            string Str = "";
            Serialize(ref Str);
            Arr[i] = Str;
        }
    }

    public string[] SerializeStringArray()
    {
        int Count = 0;
        Serialize(ref Count);
        if (Count < 1) return Array.Empty<string>();

        string[] Arr = new string[Count];
        for (int i = 0; i < Count; i++)
        {
            string Str = "";
            Serialize(ref Str);
            Arr[i] = Str;
        }
        return Arr;
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

        // Prepare list capacity to avoid multiple re-allocations
        Container.Capacity = Math.Max(Container.Capacity, Count);

        // We use a trick with CollectionsMarshal to get direct access to the List's internal array
        // To do this safely, we must first set the size of the list.
        // In modern .NET, we can use CollectionsMarshal.SetCount (if on .NET 8+) 
        // or simply add dummy elements.
        for (int i = 0; i < Count; i++) Container.Add(default);

        Span<TElementType> ElementSpan = CollectionsMarshal.AsSpan(Container);

        // Get a pointer to the start of the List's internal array
        fixed (TElementType* DstPtr = &ElementSpan[0])
        {
            // Copy directly from our unmanaged Buffer to the List's memory
            // No need to pin 'Buffer' as it is already a byte*
            Unsafe.CopyBlock(DstPtr, Buffer + PrivatePos, (uint)BytesToRead);
        }

        PrivatePos += BytesToRead;
    }
    #endregion

    public virtual IObject? SerializeObject() { return null; }




    protected void Resize(int NewLength)
    {
        if (NewLength > Length)
        {
            if (Buffer != null)
            {
                NativeMemory.AlignedFree(Buffer);
            }

            Buffer = (byte*)NativeMemory.AlignedAlloc((nuint)NewLength, 64);
            Length = NewLength;
        }
    }


    public Span<byte> AsSpan() => new Span<byte>(Buffer, Length);

    public ReadOnlySpan<byte> AsReadOnlySpan() => new ReadOnlySpan<byte>(Buffer, Length);

    public ReadOnlySpan<byte> AsReadOnlySpan(int Length)
    {
        if (Pos + Length > this.Length)
        {
            throw new InvalidOperationException($"Pos + Length is more than the buffer length. Pos: {Pos} Length: {Length}");
        }
        return new ReadOnlySpan<byte>(Buffer + Pos, Length);
    }

    public ReadOnlySpan<byte> AsReadOnlySpan(int Start, int Length)
    {
        if (Start + Length > this.Length)
        {
            throw new InvalidOperationException($"Start + Length is more than the buffer length. Start: {Start} Length: {Length}");
        }
        return new ReadOnlySpan<byte>(Buffer + Start, Length);
    }

    public byte* GetBuffer()
    {
        return Buffer;
    }

    public byte[] GetBufferAsManaged()
    {
        if (PrivateNum == 0) return Array.Empty<byte>();

        byte[] ManagedArray = new byte[PrivateNum];
        fixed (byte* DstPtr = ManagedArray)
        {
            Unsafe.CopyBlock(DstPtr, Buffer, (uint)PrivateNum);
        }
        return ManagedArray;
    }


    

    protected override void Dispose(bool Disposing) => Dispose();

    public override Span<byte> GetSpan() => new Span<byte>(Buffer, Length);
    public override MemoryHandle Pin(int ElementIndex = 0)
    {
        if (ElementIndex < 0 || ElementIndex >= Length)
            throw new ArgumentOutOfRangeException();

        return new MemoryHandle(Buffer + ElementIndex);
    }

    public override void Unpin() { }

    public virtual void Dispose()
    {
        if (Buffer != null)
        {
            NativeMemory.AlignedFree(Buffer);
            Buffer = null;
        }
    }
}