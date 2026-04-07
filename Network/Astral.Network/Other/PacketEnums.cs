namespace Astral.Network.Enums;

[Flags]
public enum EPacketFlags : byte
{
    None = 0,
    Reliable = 1 << 0,
    HasAcks = 1 << 1,
    HasAcksOnly = 1 << 2,
    Fragment = 1 << 3,
    HasTimestamp = 1 << 4,
}

public enum EProtocolMessage : byte
{
    None = 0,
    Connect,
    Ping,
    Pong,
    Unreliable,
    Reliable,
}

public enum EPacketMessage : byte
{
    Connect,
    Reliable,
    Unreliable,
}

[Flags]
public enum EConnectionPacketFlags : byte
{
    None = 0,
    HasMappings = 1 << 0,
    HasObjectCreations = 1 << 1,
    HasObjectAcks = 1 << 2,
}