using Astral.Network.Enums;
using Astral.Serialization;

namespace Astral.Network.Transport;

public abstract class InPacket : ByteReader
{
    public Neta_PacketIdType Id;
    public EPacketFlags Flags;
    internal EProtocolMessage Message;

#pragma warning disable CS8618
    protected InPacket() { }
}