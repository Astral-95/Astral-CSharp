using Astral.Interfaces;
using Astral.Network.Connections;
using Astral.Network.Interfaces;
using Astral.Network.Servers;

namespace Astral.Network.Drivers;

public partial class NetaDriver : IObject
{
    public bool IsServer { get; set; } = false;
    public static int ConnectTimeout = 7500;

    List<INetworkObject> NetworkObjects { get; set; }
    public List<INetworkObject> DirtyObjects = new List<INetworkObject>(1024);

    public NetaServer? Server { get; internal set; }
    public ServerConnection? Client { get; internal set; }

    private const long NetworkIdCooldownTicks = 60 * TimeSpan.TicksPerSecond;
    private readonly Queue<(UInt32 Id, long ReturnedAt)> CoolingNetworkIds = new();
    private ushort NextNetworkId = 32768;

    public NetaDriver()
    {
        AddDefualtSubobject(this);
    }

    public void ClearDirtyBridges() => DirtyObjects.Clear();
    public void AddDirtyObject(INetworkObject Obj) => DirtyObjects.Add(Obj);



    public UInt32 RentNetworkId()
    {
        if (CoolingNetworkIds.TryPeek(out var Entry) && DateTime.UtcNow.Ticks - Entry.ReturnedAt >= NetworkIdCooldownTicks)
        {
            return CoolingNetworkIds.Dequeue().Id;
        }

        if (NextNetworkId == ushort.MaxValue)
            throw new InvalidOperationException("NetworkId pool exhausted.");

        return NextNetworkId++;
    }

    public void ReturnNetworkId(UInt32 NetworkId)
    {
        if (NetworkId < 2) return;
        CoolingNetworkIds.Enqueue((NetworkId, DateTime.UtcNow.Ticks));
    }


    public virtual void AddNetworkObject(INetworkObject Obj)
    {
        if (Obj.NetworkId > 0) return;
        NetworkObjects.Add(Obj);

        var NewNetId = RentNetworkId();

        Obj.InitNetworkObject(this);

        Obj.ISetNetworkId(NewNetId);
    }

    public virtual void RemoveNetworkObject(INetworkObject Obj)
    {
        if (Obj.NetworkId < 1) return;
        ReturnNetworkId(Obj.NetworkId);
        Obj.ISetNetworkId(0);

    }

}