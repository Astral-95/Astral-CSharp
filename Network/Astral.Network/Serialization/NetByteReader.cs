using Astral.Network.PackageMaps;
using Astral.Interfaces;
using Astral.Serialization;

namespace Astral.Network.Serialization;

public class NetByteReader : ByteReader
{
    public ConnectionPackageMap? PackageMap;
    internal protected NetByteReader() { }
    internal protected NetByteReader(ConnectionPackageMap PackageMap, int NunBytes) : base(NunBytes)
    {
        this.PackageMap = PackageMap;
    }

    public NetByteReader(NetByteReader Reader) : base(Reader) { }
    public NetByteReader(ConnectionPackageMap PackageMap, byte[] Buffer, Int32 LengthBits) : base(Buffer, LengthBits)
    {
        this.PackageMap = PackageMap;
    }

    public override IObject? SerializeObject()
    {
        if (PackageMap == null) return null;
        return PackageMap.SerializeObject(this);
    }
}