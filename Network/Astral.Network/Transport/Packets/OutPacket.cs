using Astral.Network.Enums;
using Astral.Serialization;

namespace Astral.Network.Transport;

public class DebugPacketSettings
{
    public bool FixedBodySize = true;
    public int FixedBodySizeBytes = 128;
    public int MinBodySizeBytes = 128;
    public int MaxBodySizeBytes = 128;
}

public abstract class OutPacket : ByteWriter
{
    protected long InPool = 0;
    public bool FinalizeCalled { get; protected set; } = false;
    public Neta_PacketIdType Id { get; protected set; }
    public EPacketFlags Flags { get; internal set; }
    public EProtocolMessage Message { get; internal set; }


    //public double DeadlineTicks { get; internal set; }  // This packet's current timeout
    //public int Retries { get; internal set; }
    public int HeaderBytes { get; internal set; }

    protected OutPacket(int NumBytes = NetaConsts.BufferMaxSizeBytes) : base(NumBytes) { }

    protected OutPacket(Neta_PacketIdType Id, int NumBytes = NetaConsts.BufferMaxSizeBytes, EProtocolMessage Message = EProtocolMessage.None) : base(NumBytes)
    {
        this.Id = Id;
        this.Message = Message;
        HeaderBytes = NetaConsts.HeaderSizeBytes;

        Serialize<Neta_PacketSizeType>(0);
        Serialize(this.Id);
        Serialize(Flags, 1);
        Serialize(Message, 1);
    }


    internal protected void FinalizePacket()
    {
        FinalizeCalled = true;

        var OldPos = (Neta_PacketSizeType)Pos;
        SetPos(0);
        Serialize(OldPos);

        SetPos(NetaConsts.PacketFlagsPos);
        Serialize(Flags, 1);
        Serialize(Message, 1);

        SetPos(OldPos);
    }

    internal void FinalizeReliablePacket(Int64 Timestamp)
    {
        FinalizeCalled = true;

        var OldPos = (Neta_PacketSizeType)Pos;
        SetPos(0);
        Serialize(OldPos);

        SetPos(NetaConsts.PacketFlagsPos);
        Serialize(Flags, 1);
        Serialize(Message, 1);
        Serialize<Int64>(Timestamp);

        SetPos(OldPos);
    }

    internal void UpdateTimestamp(Int64 Timestamp)
    {
        var OldPos = (Neta_PacketSizeType)Pos;
        SetPos(NetaConsts.TimestampPos);
        Serialize<Int64>(Timestamp);
        SetPos(OldPos);
    }

    internal void FinalizePacketAsFragment(Int64 Timestamp)
    {
        if (Interlocked.Read(ref InPool) != 0)
        {
            throw new InvalidOperationException("Packate is in the pool.");
        }

        if (FinalizeCalled)
        {
            throw new InvalidOperationException("Finalize called more than once.");
        }

        if (Pos < 1)
        {
            throw new InvalidOperationException("Finlizing an empty packet.");
        }

        FinalizeCalled = true;

        var OldPos = (Neta_PacketSizeType)Pos;
        SetPos(0);
        Serialize(OldPos);

        SetPos(NetaConsts.PacketFlagsPos);
        Serialize(Flags, 1);
        Serialize(Message, 1);
        Serialize<Int64>(Timestamp);
        SetPos(OldPos);
    }
}