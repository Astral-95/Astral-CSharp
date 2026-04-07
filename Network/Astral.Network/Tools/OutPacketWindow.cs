using Astral.Network.Transport;
using System.Runtime.InteropServices;

namespace Astral.Network.Tools;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ReliableSlot
{
    public Neta_PacketIdType PacketId;
    public long DeadlineTicks;
    public PooledOutPacket? Packet;
    public int NumTries;
    public bool IsPending;
}

public class OutPacketWindow
{
    private readonly ReliableSlot[] Slots;
    private readonly int Mask;
    public int PendingCount { get; private set; } = 0;

    private const int MaxRetries = 15; // tune if you want more/less aggressive drop

    /// <summary>
    /// WindowSize MUST be a power of 2.
    /// </summary>
    public OutPacketWindow(int WindowSize = 2048)
    {
        if ((WindowSize & (WindowSize - 1)) != 0)
            throw new ArgumentException("WindowSize must be a power of 2.");

        Slots = new ReliableSlot[WindowSize]; // structs are zero-initialized → IsPending=false, Packet=null, etc.
        Mask = WindowSize - 1;
    }

    /// <summary>
    /// Registers a packet for reliability.
    /// Returns false if the window is full OR overflowed (old unacked packet blocking the slot).
    /// → Caller should disconnect in this case.
    /// </summary>
    public bool AddPending(PooledOutPacket Packet, long Deadlineticks)
    {
        int Index = Packet.Id & Mask;
        ref var Slot = ref Slots[Index];

        if (Slot.IsPending)
        {
            // Window full or wrap-around overflow (old packet never ACKed)
            // This is the signal to disconnect
            return false;
        }

        Slot.PacketId = Packet.Id;
        Slot.Packet = Packet;
        Slot.DeadlineTicks = Deadlineticks;
        Slot.IsPending = true;
        Slot.NumTries = 0;

        PendingCount++;
        return true;
    }

    /// <summary>
    /// Marks a packet as acknowledged. Cleans the slot immediately.
    /// </summary>
    public bool Acknowledge(Neta_PacketIdType PacketId, out int NumTries)
    {
        int Index = PacketId & Mask;
        ref var Slot = ref Slots[Index];

        if (!Slot.IsPending || Slot.PacketId != PacketId)
        {
            NumTries = 0;
            return false;
        }

        NumTries = Slot.NumTries;

        Slot.IsPending = false;
        Slot.Packet?.Return();
        Slot.Packet = null;
        Slot.NumTries = 0;

        PendingCount--;
        return true;
    }

    /// <summary>
    /// Call once per server tick.
    /// Handles retransmissions + drops packets that hit MaxRetries (link is dead).
    /// </summary>
    public void Sweep(long TicksNow, long RTO, Action<PooledOutPacket> Callback)
    {
        for (int i = 0; i < Slots.Length; i++)
        {
            ref var Slot = ref Slots[i];
            if (!Slot.IsPending) continue;

            if (TicksNow >= Slot.DeadlineTicks)
            {
                if (Slot.NumTries >= MaxRetries)
                {
                    // Packet permanently failed → free slot so window doesn't clog forever
                    Slot.Packet?.Return();
                    Slot.Packet = null;
                    Slot.IsPending = false;
                    Slot.NumTries = 0;
                    PendingCount--;
                    continue;
                }

                Slot.NumTries++;

                // Fast exponential backoff (1 << N) with cap
                int cappedTries = Math.Min(Slot.NumTries, 30);
                long Backoff = 1L << cappedTries;

                long NewRto = RTO * Backoff;
                if (NewRto > PacketStatistics.MaxRetransmissionTimeoutTicks)
                    NewRto = PacketStatistics.MaxRetransmissionTimeoutTicks;

                Slot.DeadlineTicks = TicksNow + NewRto;
                Slot.Packet!.UpdateTimestamp(TicksNow);

                Callback(Slot.Packet!);   // <-- retransmit
            }
        }
    }
}