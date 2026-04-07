namespace Astral.Network.Tools;


public enum PacketWindowStatus
{
    New = 0,            // Valid, first time seeing it
    Duplicate = 1,      // Valid ID, but already processed
    TooOld = 2,         // Outside the trailing edge (Replay risk)
    TooFarAhead = 3,    // Outside the max forward jump (Adversarial risk)
    Invalid = 4         // Exact match of HighestReceived or other logic error
}

//public sealed class PacketWindow
//{
//    private readonly object LockObject = new();
//    private const int WindowSize = 8192;                // power of two
//    private const int BitsPerWord = NetaConsts.PacketIdSizeBytes * 8;        // PacketId size bits
//    private const int NumWords = WindowSize / BitsPerWord;
//    private const int WindowMask = WindowSize - 1;
//    private const int BitMask = BitsPerWord - 1;
//
//    private readonly ulong[] Bitmap = new ulong[NumWords];
//    internal ulong HighestReceived = 0UL;
//
//    // Set to ulong.MaxValue to effectively disable malicious-ahead check.
//    public ulong MaxAllowedAhead { get; set; } = WindowSize * 8UL;
//
//    public bool SeenBefore(ulong PacketId)
//    {
//        lock (LockObject)
//        {
//            // Late or in-window
//            if (PacketId <= HighestReceived)
//            {
//                ulong Distance = HighestReceived - PacketId;
//                if (Distance >= WindowSize)
//                    throw new PacketTooOldException(PacketId, HighestReceived);
//
//                int Offset = (int)(PacketId & WindowMask);
//                int WordIndex = Offset >> 6;
//                int BitIndex = Offset & BitMask;
//                ulong Mask = 1UL << BitIndex;
//
//                if ((Bitmap[WordIndex] & Mask) != 0UL)
//                    return true;
//                Bitmap[WordIndex] |= Mask;
//                return false;
//            }
//
//            // PacketId > HighestReceived (advance)
//            ulong Diff = PacketId - HighestReceived;
//            if (Diff > MaxAllowedAhead)
//                throw new InvalidPacketException($"PacketId {PacketId} is unreasonably far ahead (Diff {Diff}). Highest: {HighestReceived}");
//
//            if (Diff >= WindowSize)
//            {
//                // Everything expired — cheap full clear
//                Array.Clear(Bitmap, 0, NumWords);
//            }
//            else
//            {
//                // CLEAR THE LOW SIDE: the sequence numbers that fell out of the window
//                // OldLowest = HighestReceived - (WindowSize - 1)
//                ulong OldLowest = HighestReceived - (WindowSize - 1);
//                for (ulong i = 0; i < Diff; i++)
//                {
//                    int Offset = (int)(OldLowest + i & WindowMask);
//                    int WordIndex = Offset >> 6;
//                    int BitIndex = Offset & BitMask;
//                    Bitmap[WordIndex] &= ~(1UL << BitIndex);
//                }
//            }
//
//            // Install new Highest and mark this PacketId
//            HighestReceived = PacketId;
//            int NewOffset = (int)(PacketId & WindowMask);
//            int NewWord = NewOffset >> 6;
//            int NewBit = NewOffset & BitMask;
//            ulong NewMask = 1UL << NewBit;
//
//            if ((Bitmap[NewWord] & NewMask) != 0UL)
//                return true;
//            Bitmap[NewWord] |= NewMask;
//            return false;
//        }
//    }
//}


public sealed class InPacketWindow
{
    public const int WindowSize = 8192;
    private const int WindowMask = WindowSize - 1;
    private const int BitmapLen = WindowSize / 64;   // 128 ulongs

    private readonly ulong[] _bitmap = new ulong[BitmapLen];
    private bool _hasStarted;

    public ushort SequenceHead { get; private set; }

    private uint _maxAllowedAhead = WindowSize / 2;

    /// Hard-capped at WindowSize-1 so a single adversarial packet
    /// can never wipe the entire bitmap.
    public uint MaxAllowedAhead
    {
        get => _maxAllowedAhead;
        set => _maxAllowedAhead = Math.Clamp(value, 1u, (uint)(WindowSize - 1));
    }

    public InPacketWindow()
    {
        SequenceHead = 0;
        Mark(0);
    }

    /// Returns true  → duplicate or out-of-window, drop it.
    /// Returns false → new packet, process it.
    public PacketWindowStatus CheckPacket(ushort packetId)
    {
        // 1. Calculate modular distance
        ushort diff = (ushort)(packetId - SequenceHead);

        // 2. Exact match of the current head
        if (diff == 0) return PacketWindowStatus.Duplicate;

        // 3. CASE: FORWARD (Newer than Head)
        if (diff <= 32767)
        {
            if (diff > _maxAllowedAhead)
                return PacketWindowStatus.TooFarAhead;

            ClearRange(SequenceHead, diff);  // ← replaces the for loop
            SequenceHead = packetId;
            Mark(packetId);
            return PacketWindowStatus.New;

            //// Is the jump suspiciously large?
            //if (diff > _maxAllowedAhead)
            //    return PacketWindowStatus.TooFarAhead;
            //
            //// Clear the path. Since diff is capped by WindowSize, 
            //// this loop is bounded and won't stall the CPU.
            //for (int i = 1; i <= diff; i++)
            //{
            //    Clear((ushort)(SequenceHead + i));
            //}
            //
            //SequenceHead = packetId;
            //Mark(packetId);
            //return PacketWindowStatus.New;
        }

        // Check our bitmap memory
        if (IsMarked(packetId))
            return PacketWindowStatus.Duplicate;

        // 4. CASE: BACKWARD (Older than Head)
        ushort distance = (ushort)(SequenceHead - packetId);
        
        if (distance >= WindowSize)
            return PacketWindowStatus.TooOld;

        // It's a late packet but within the window
        Mark(packetId);
        return PacketWindowStatus.New;
    }

    public void Reset()
    {
        _hasStarted = false;
        SequenceHead = 0;
        Array.Clear(_bitmap, 0, BitmapLen);
    }

    private void Mark(ushort id)
    {
        int s = id & WindowMask;
        _bitmap[s >> 6] |= 1UL << (s & 63);
    }

    private void Clear(ushort id)
    {
        int s = id & WindowMask;
        _bitmap[s >> 6] &= ~(1UL << (s & 63));
    }

    private bool IsMarked(ushort id)
    {
        int s = id & WindowMask;
        return (_bitmap[s >> 6] & (1UL << (s & 63))) != 0;
    }

    private void ClearRange(ushort fromExclusive, int count)
    {
        int start = (fromExclusive + 1) & WindowMask;
        int end = (start + count - 1) & WindowMask;

        if (start <= end)
        {
            ClearBitmapRange(start, end);
        }
        else
        {
            // Wraps around: clear [start..8191] then [0..end]
            ClearBitmapRange(start, WindowMask);
            ClearBitmapRange(0, end);
        }
    }

    private void ClearBitmapRange(int start, int end)
    {
        // start <= end guaranteed, both in [0..8191]
        int wordStart = start >> 6;
        int wordEnd = end >> 6;
        int bitStart = start & 63;
        int bitEnd = end & 63;

        if (wordStart == wordEnd)
        {
            ulong mask = (bitEnd == 63 ? ulong.MaxValue : ((1UL << (bitEnd + 1)) - 1))
                       & ~((1UL << bitStart) - 1);
            _bitmap[wordStart] &= ~mask;
            return;
        }

        _bitmap[wordStart] &= (1UL << bitStart) - 1;

        if (wordEnd > wordStart + 1)
            Array.Clear(_bitmap, wordStart + 1, wordEnd - wordStart - 1);

        ulong tailMask = bitEnd == 63 ? ulong.MaxValue : (1UL << (bitEnd + 1)) - 1;
        _bitmap[wordEnd] &= ~tailMask;
    }
}