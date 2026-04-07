using Astral.Interfaces;
using Astral.Network.Drivers;
using Astral.Network.Serialization;

namespace Astral.Network.Interfaces;

[Flags]
public enum NetworkObjectFlags
{
    None,
    IsReplicating,
    Dirty,
}

public interface INetworkObject : IObject
{
    public UInt32 NetworkId { get; }
    public NetworkObjectFlags INetworkFlags { get; set; }

    public List<WeakReference<INetworkObject>> ReferencedObjects { get; }
    public List<WeakReference<INetworkObject>> DeltaReferencedObjects { get; }


    public void ISetNetworkId(UInt32 NewId);

    public void InitNetworkObject(NetaDriver Driver);

    public void CreatedFromRemote();
    public void AssignedFromRemote();
    public void PreDestroyFromRemote() { }

    // Call this when the object is about to be destroyed.
    public void PreDestroy() { }

    public void EnqueueRemoteCall(int MethodIndex, PooledNetByteWriter? Writer);
    public void EnqueueRemoteCall_Reliable(int MethodIndex, PooledNetByteWriter? Writer);
    public void EnqueueRemoteCall_ReliableOrdered(int MethodIndex, PooledNetByteWriter? Writer);

    public void EnqueueMulticastRemoteCall(int MethodIndex, PooledNetByteWriter? Writer);
    public void EnqueueMulticastRemoteCall_Reliable(int MethodIndex, PooledNetByteWriter? Writer);
    public void EnqueueMulticastRemoteCall_ReliableOrdered(int MethodIndex, PooledNetByteWriter? Writer);
}