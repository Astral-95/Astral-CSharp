using Astral.Interfaces;
using Astral.Network.Interfaces;
using Astral.Serialization;

namespace Astral.Network.Serialization;

public class NetByteWriter : ByteWriter
{
    public List<IObject> ReferencedObjects = new List<IObject>(16);

    public NetByteWriter(Int32 InitialSizeBytes = 64) : base(InitialSizeBytes) { }

    public override void SerializeObject(IObject? Obj)
    {
        UInt32 NetId = 0;
        if (Obj is INetworkObject NetObject)
        {
            NetId = NetObject.NetworkId;
            ReferencedObjects.Add(Obj);
        }
        Serialize<UInt32>(NetId);
    }


    public override void Reset(Int32 MinBytes = 64)
    {
        if (MinBytes < 0) throw new Exception("BitWriter: Buffer size cannot be negative.");

        if (Num < MinBytes)
        {
            Buffer = new byte[MinBytes];
            Num = MinBytes;
        }

        Pos = 0;

        ReferencedObjects.Clear();
    }
}