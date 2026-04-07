global using Neta_PacketIdType = System.UInt16;
global using Neta_PacketSizeType = System.UInt16;
global using Neta_PacketTimestampType = System.Int64;

global using Neta_FragmentIndexType = System.Byte;
global using Neta_FragmentGroupIndexType = System.UInt16;
global using Neta_FragmentNumFragsType = System.Byte;



global using Neta_BunchIdType = System.UInt64;
global using Neta_BunchSizeType = System.UInt16;
global using Neta_BunchCountType = System.UInt16;


global using Neta_ChannelFlagsType = System.Byte;
global using Neta_ChannelIndexType = System.Byte;


namespace Astral.Network;

public static class NetaConsts
{
    public static readonly byte[] CloseBufferr = Array.Empty<byte>();
    public const int ListCountSizeBytes = 4;
    public const int ArrayCountSizeBytes = 4;

    public const int PacketNumBytesSizeBytes = sizeof(Neta_PacketSizeType);
    public const int PacketIdSizeBytes = sizeof(Neta_PacketIdType);
    public const int PacketFlagsSizeBytes = 1;
    public const int PacketMessagedSizeBytes = 1;
    public const int PacketTimestampSizeBytes = sizeof(Neta_PacketTimestampType);

    public const int AckSizeBytes = sizeof(Neta_PacketSizeType) + sizeof(Neta_PacketTimestampType) + sizeof(Neta_PacketTimestampType);

    public const int PacketFlagsPos = PacketIdSizeBytes + PacketNumBytesSizeBytes;
    public const int PacketMessagePos = PacketNumBytesSizeBytes + PacketIdSizeBytes + PacketFlagsSizeBytes;
    public const int TimestampPos = PacketNumBytesSizeBytes + PacketIdSizeBytes + PacketFlagsSizeBytes + PacketMessagedSizeBytes;

    public const int HeaderSizeBytes = PacketNumBytesSizeBytes + PacketIdSizeBytes + PacketFlagsSizeBytes + PacketMessagedSizeBytes;
    public const int ReliableHeaderSizeBytes = PacketNumBytesSizeBytes + PacketIdSizeBytes + PacketFlagsSizeBytes + PacketMessagedSizeBytes + PacketTimestampSizeBytes;


    /// <summary>
    /// FragId + SeqId + NumFrags 
    /// </summary>
    //public const int FragHeaderSizeBytes = 4;
    public const int PartialHeaderSizeBytes = sizeof(Neta_FragmentIndexType) + sizeof(Neta_FragmentGroupIndexType) + sizeof(Neta_FragmentNumFragsType);


    //public const int FragPayloadMaxSizeBytes = BufferMaxSizeBytes - (HeaderSizeBytes + PartialHeaderSizeBytes);

    public const int BufferMaxSizeBytes = 1024;

    public const int AckPiggybackMaxCount = BufferMaxSizeBytes / AckSizeBytes / 2;
    public const int AckPerPacketMaxCount = (int)(BufferMaxSizeBytes / AckSizeBytes / 1.5f);



    public const int BunchesOrderedPos = 0;
    public const int BunchesIdPos = 0;


    public const int BunchesCountSizeBytes = 2;
    public const int BuncSizeTypeSizeBytes = 4;

    public const int BunchMaxSizeBytes = BufferMaxSizeBytes - HeaderSizeBytes - 256;
    //public const int BunchMaxSizeBytes = 0;

    public const int ConnectionChannelsReserve = 256;
}