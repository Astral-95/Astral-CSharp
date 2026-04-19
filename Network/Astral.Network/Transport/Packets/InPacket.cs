using Astral.Network.Enums;
using Astral.Serialization;

namespace Astral.Network.Transport;

public abstract class InPacket : UnmanagedByteReader
{
    public Neta_PacketIdType Id;
    public EPacketFlags Flags;
    internal EProtocolMessage Message;

#pragma warning disable CS8618
    protected InPacket() { }
    //protected InPacket(int NumBytes) : base(NumBytes) { }
}