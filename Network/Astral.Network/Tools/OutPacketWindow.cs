using Astral.Network.Transport;

namespace Astral.Network.Tools;

public struct ReliableSlot
{
    public bool IsPending;
    public int PendingListIndex;          // back-reference into _pendingIndices for O(1) remove
    public Neta_PacketIdType PacketId;
    public long DeadlineTicks;
    public PooledOutPacket? Packet;
    public int NumTries;
}

public sealed class OutPacketWindow
{
    private readonly ReliableSlot[] _slots;
    private readonly int _mask;

    // Dense pending-only list: Sweep iterates only these indices, not all 2048 slots
    private readonly int[] _pendingIndices;
    private int _pendingCount;

    public int PendingCount => _pendingCount;

    private const int MaxRetries = 15;

    /// <summary>
    /// WindowSize MUST be a power of 2.
    /// </summary>
    public OutPacketWindow(int windowSize = 2048)
    {
        if ((windowSize & (windowSize - 1)) != 0)
            throw new ArgumentException("WindowSize must be a power of 2.");

        _slots = new ReliableSlot[windowSize];
        _pendingIndices = new int[windowSize];   // worst case: every slot pending
        _mask = windowSize - 1;
    }

    /// <summary>
    /// Registers a packet for reliability tracking.
    /// Returns false if the slot is already occupied (window full or wrap-around overflow).
    /// Caller should disconnect on false.
    /// </summary>
    public bool AddPending(PooledOutPacket packet, long deadlineTicks)
    {
        int index = packet.Id & _mask;
        ref var slot = ref _slots[index];

        if (slot.IsPending)
            return false;

        slot.PacketId = packet.Id;
        slot.Packet = packet;
        slot.DeadlineTicks = deadlineTicks;
        slot.NumTries = 0;
        slot.PendingListIndex = _pendingCount;  // where in the list it'll live
        slot.IsPending = true;           // set last (visibility fence)

        _pendingIndices[_pendingCount++] = index;
        return true;
    }

    /// <summary>
    /// Marks a packet acknowledged and immediately frees the slot.
    /// O(1): swap-remove via PendingListIndex back-reference.
    /// </summary>
    public bool Acknowledge(Neta_PacketIdType packetId, out int numTries)
    {
        int index = packetId & _mask;
        ref var slot = ref _slots[index];

        if (!slot.IsPending || slot.PacketId != packetId)
        {
            numTries = 0;
            return false;
        }

        numTries = slot.NumTries;

        slot.Packet?.Return();
        slot.Packet = null;
        slot.IsPending = false;

        RemoveFromPendingList(slot.PendingListIndex);
        return true;
    }

    /// <summary>
    /// Call once per server tick. Retransmits and drops dead packets.
    /// O(pending) — only touches live slots.
    /// </summary>
    public void Sweep(long ticksNow, long rto, Action<PooledOutPacket> callback)
    {
        // Iterate backwards so swap-remove doesn't skip entries
        for (int j = _pendingCount - 1; j >= 0; j--)
        {
            int i = _pendingIndices[j];
            ref var slot = ref _slots[i];

            if (ticksNow < slot.DeadlineTicks)
                continue;

            if (slot.NumTries >= MaxRetries)
            {
                // Link is dead — free the slot and evict from list
                slot.Packet?.Return();
                slot.Packet = null;
                slot.IsPending = false;
                RemoveFromPendingList(j);   // j is already the list position
                continue;
            }

            slot.NumTries++;

            // Exponential backoff, capped at 30 shifts to avoid overflow
            int cappedTries = Math.Min(slot.NumTries, 30);
            long backoff = 1L << cappedTries;
            long newRto = rto * backoff;

            if (newRto > PacketStatistics.MaxRetransmissionTimeoutTicks)
                newRto = PacketStatistics.MaxRetransmissionTimeoutTicks;

            slot.DeadlineTicks = ticksNow + newRto;
            slot.Packet!.UpdateTimestamp(ticksNow);

            callback(slot.Packet!);
        }
    }

    /// <summary>
    /// O(1) swap-remove. Moves the last entry into position j and fixes its back-reference.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void RemoveFromPendingList(int listPosition)
    {
        int last = _pendingCount - 1;

        if (listPosition != last)
        {
            // Move the last slot's index into the vacated position
            int movedSlotIndex = _pendingIndices[last];
            _pendingIndices[listPosition] = movedSlotIndex;

            // Fix the moved slot's back-reference
            _slots[movedSlotIndex].PendingListIndex = listPosition;
        }

        _pendingCount--;
    }
}