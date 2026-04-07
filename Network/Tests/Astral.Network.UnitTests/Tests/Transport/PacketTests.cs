using Astral.Network.Transport;
using Astral.Network.UnitTests.Tools;
using Astral.Serialization;
using System.Runtime.InteropServices;

namespace Astral.Network.UnitTests.Transport;

[Collection("DisableParallelizationCollection")]
public class PacketTests
{
    class PacketTests_OutInTest_1 { }
    class PacketTests_OutInTest_2 { }
    [Fact]
    public async Task OutInTestAsync()
    {
        await using var _ = await AsyncScopeLock.LockAsync();
        PooledObjectsTracker.ClearForTests();
        string Str = "Hello";
        var PacketOut = PooledOutPacket.Rent<PacketTests_OutInTest_1>(1, Enums.EProtocolMessage.Connect);

        ByteWriter Writer = new ByteWriter();
        Writer.Serialize(Str);

        PacketOut.Serialize(Writer);
        PacketOut.FinalizePacket();

        var PacketIn = PooledInPacket.Rent<PacketTests_OutInTest_2>();
        Buffer.BlockCopy(PacketOut.GetBuffer(), 0, PacketIn.GetBuffer(), 0, PacketOut.Pos);

        var NumBytes = MemoryMarshal.Read<ushort>(PacketIn.GetBuffer());
        PacketIn.Num = NumBytes;
        PacketIn.Init();
        var Reader = new ByteReader(PacketIn);
        Assert.Equal(Reader.SerializeString(), Str);
        PacketOut.Return();
        PacketIn.Return();

        var Leaks = PooledObjectsTracker.ReportLeaks();
        if (Leaks != null) Assert.Fail(string.Join("\n", Leaks));
    }
}