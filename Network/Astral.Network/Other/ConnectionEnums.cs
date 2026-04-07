
namespace Astral.Network.Enums;

public enum EConnectionPacketMessage
{
    Control,
    Connect,
    Remote,
    Channel,
    ChannelOpen,
    ChannelClose,
}

[Flags]
public enum NetaConnectionFlags
{
    None = 0,
    Pending = 1 << 1,
    Connected = 1 << 2,
    Shutdown = 1 << 3,
    Server = 1 << 4,
    Handshaking = 1 << 6,
    Reconnecting = 1 << 7,
    TimedOut = 1 << 8,
    Dormant = 1 << 9,
    Error = 1 << 10,
    VoiceEnabled = 1 << 11,
    SimulatedLag = 1 << 12,
    Cleaned = 1 << 13,
}