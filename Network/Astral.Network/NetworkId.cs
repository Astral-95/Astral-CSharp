using Astral.Serialization;

namespace Astral.Network;

public struct NetworkId
{
    UInt64 Id;


    void Serialize(ByteWriter Writer)
    {
        Writer.Serialize(Id);
    }

    void Serialize(ByteReader Reader)
    {
        Id = Reader.Serialize<UInt64>();
    }
}