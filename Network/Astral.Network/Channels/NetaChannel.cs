using Astral.Attributes;
using Astral.Network.PackageMaps;
using Astral.Network.Connections;
using Astral.Network.Drivers;
using Astral.Network.Interfaces;
using Astral.Network.Transport;

namespace Astral.Network.Channels;

public partial class NetaChannel : INetworkObject
{
    public NetaDriver Driver { get; private set; }
    public NetaConnection Connection { get; private set; }
    public ConnectionPackageMap PackageMap { get; private set; }
    public Neta_ChannelIndexType ChannelIndex { get; internal set; } = 0;

    Neta_BunchIdType PrivateNextBunchId = 0;
    internal Neta_BunchIdType NextBunchId { get => Interlocked.Increment(ref PrivateNextBunchId); }
    internal Neta_BunchIdType NextOrderedBunchId { get; set; } = 1;

    private readonly object PendingBunchesLock = new();
    private SortedDictionary<Neta_BunchIdType, InBunch> PendingBunches = new();

    internal Neta_BunchIdType NumDebugPacketsProcessed = 0;

    public NetaChannel(NetaConnection Connection, byte InIndex)
    {
        this.Connection = Connection;
        this.Driver = Connection.Driver;
        this.ChannelIndex = InIndex;
        PackageMap = Connection.PackageMap;

        PackageMap = Connection.PackageMap;
    }


    class NetaChannel_Receive_InBunch_1 { }
    class NetaChannel_Receive_InBunch_2 { }
    internal void Receive_InBunch(InBunch Bunch)
    {
        if (Bunch.Id == 0)
        {
            Process_InBunch(Bunch);
            Bunch.Return<NetaChannel_Receive_InBunch_1>();
            return;
        }

        lock (PendingBunchesLock)
        {
            PendingBunches[Bunch.Id] = Bunch;

            while (PendingBunches.TryGetValue(NextOrderedBunchId, out var NextBunch))
            {
                Process_InBunch(NextBunch);
                PendingBunches.Remove(NextOrderedBunchId);
                NextOrderedBunchId++;
                NextBunch.Return<NetaChannel_Receive_InBunch_2>();
            }
        }
    }

    void Process_InBunch(InBunch Bunch)
    {
        Connection.PacketStats.IncrementAppIn();

        var Obj = Bunch.SerializeObject();

        if (Obj == null)
        {
            return;
            //throw new InvalidBunchException(Bunch, $"Invalid net object Id\n\tBunchId: {Bunch.Id} BunchBytes: {Bunch.Reader.Pos}");
        }

        Int32 MethodIndex = Bunch.Serialize<Int32>();

        InvokeMethod(MethodIndex, Bunch);
    }

    public OutBunch CreateBunch<T>()
    {
        var NewBunch = OutBunch.Rent<T>(this);

        return NewBunch;
    }

    public OutBunch CreateReliableBunch<T>()
    {
        var NewBunch = OutBunch.Rent<T>(this);
        NewBunch.SetIsReliable();
        return NewBunch;
    }

    //public void SendReliableDebugPacket(DebugPacketSettings Settings)
    //{
    //	int BodySize = Settings.FixedBodySize ? Settings.FixedBodySizeBytes
    //			: Random.Shared.Next(1, Settings.MaxBodySizeBytes);
    //
    //	byte[] Buffer = new byte[BodySize];
    //	for (int I = 0; I < BodySize; I++)
    //		Buffer[I] = (byte)((I & 1) == 0 ? 1 : 0);
    //
    //	DebugPacketReliable_Send(Buffer);
    //}
    //
    //public void SendUnreliableDebugPacket(DebugPacketSettings Settings)
    //{
    //	int BodySize = Settings.FixedBodySize ? Settings.FixedBodySizeBytes
    //			: Random.Shared.Next(1, Settings.MaxBodySizeBytes);
    //
    //	byte[] Buffer = new byte[BodySize];
    //	for (int I = 0; I < BodySize; I++)
    //		Buffer[I] = (byte)((I & 1) == 0 ? 1 : 0);
    //
    //	DebugPacketUnreliable_Send(Buffer);
    //}
    //
    //[Method("Remote", "Reliable", "Ordered")]
    //void DebugPacketReliable_Receive(byte[] Buffer)
    //{
    //	if (Buffer == null || Buffer.Length == 0)
    //	{
    //		throw new InvalidOperationException("Received empty debug packet buffer");
    //	}
    //
    //	for (int I = 0; I < Buffer.Length; I++)
    //	{
    //		byte Expected = (byte)((I & 1) == 0 ? 1 : 0);
    //		var Byte = Buffer[I];
    //		if (Byte != Expected)
    //		{
    //			throw new InvalidOperationException($"Debug packet mismatch at byte {I}: expected {Expected}, got {Buffer[I]}");
    //		}
    //	}
    //	Interlocked.Increment(ref NumDebugPacketsProcessed);
    //}
    //
    //[Method("Remote")]
    //void DebugPacketUnreliable_Receive(byte[] Buffer)
    //{
    //	if (Buffer == null || Buffer.Length == 0)
    //		throw new InvalidOperationException("Received empty debug packet buffer");
    //
    //	for (int I = 0; I < Buffer.Length; I++)
    //	{
    //		byte Expected = (byte)((I & 1) == 0 ? 1 : 0);
    //		var Byte = Buffer[I];
    //		if (Byte != Expected)
    //		{
    //			throw new InvalidOperationException(
    //				$"Debug packet mismatch at byte {I}: expected {Expected}, got {Buffer[I]}"
    //			);
    //		}
    //	}
    //	Interlocked.Increment(ref NumDebugPacketsProcessed);
    //}






    public void SendReliableDebugPacket(DebugPacketSettings Settings)
    {
        DebugPacketReliable_Send();
    }

    public void SendUnreliableDebugPacket(DebugPacketSettings Settings)
    {
        DebugPacketUnreliable_Send();
    }

    [Method("Remote", "Reliable", "Ordered")]
    void DebugPacketReliable_Receive()
    {
        Interlocked.Increment(ref NumDebugPacketsProcessed);
    }

    [Method("Remote")]
    void DebugPacketUnreliable_Receive()
    {
        Interlocked.Increment(ref NumDebugPacketsProcessed);
    }


    class NetaChannel_Shutdown { }
    internal protected void Shutdown()
    {
        foreach (var Pair in PendingBunches)
        {
            Pair.Value.Return<NetaChannel_Shutdown>();
        }
    }
}