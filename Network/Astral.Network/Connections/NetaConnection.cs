using Astral.Containers;
using Astral.Network.Logging;
using Astral.Network.PackageMaps;
using Astral.Network.Channels;
using Astral.Network.Drivers;
using Astral.Network.Enums;
using Astral.Network.Exceptions;
using Astral.Network.Interfaces;
using Astral.Network.Serialization;
using Astral.Network.Servers;
using Astral.Network.Sockets;
using Astral.Network.Toolkit;
using Astral.Network.Tools;
using Astral.Network.Transport;
using Astral.Tick;
using System.Net;
using System.Runtime.CompilerServices;

namespace Astral.Network.Connections;

public enum NetaConnectionMode : byte
{
    Auto,
    AutoDeferred,
    Manual
}
public partial class NetaConnection : INetworkObject
{
    protected NetaLogger Logger;
    public NetaSocket Socket { get; set; }
    public NetaDriver Driver { get; internal protected set; }
    public NetaServer? Server { get; internal protected set; }
    public int WorkerIndex { get; internal set; } = 0;
    public ConnectionPackageMap PackageMap { get; protected set; }
    public NetaChannel Channel { get; internal protected set; }

    public NetaConnectionMode Mode { get; private set; }

    int ConnectionFlags = 0;

    public event Action<NetaConnection>? OnConnectionClosed;

    internal ConcurrentStore<OutBunch> OutBunchQueue = new ConcurrentStore<OutBunch>();

    internal NetaChannel?[] Channels = new NetaChannel?[NetaConsts.ConnectionChannelsReserve];

    internal Task? ReceiveTask { get; set; }
    internal Task? DeferredReceiveTask { get; set; }

    long TickActionId;

    bool Begun = false;

    public string NetModeString { get; protected set; } = "None";

#pragma warning disable CS8618
    internal protected NetaConnection()
    {
        PrivateNetworkId = 1;

        PackageMap = new ConnectionPackageMap(this);
    }


#pragma warning restore CS8618

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ConnectionHasFlags(NetaConnectionFlags Flags)
    {
        return ((NetaConnectionFlags)Volatile.Read(ref ConnectionFlags) & Flags) == Flags;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ConnectionSetFlags(NetaConnectionFlags Flags)
    {
        Interlocked.Or(ref ConnectionFlags, (int)Flags);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ConnectionClearFlags(NetaConnectionFlags Flags)
    {
        Interlocked.And(ref ConnectionFlags, ~(int)Flags);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ConnectionFlipFlags(NetaConnectionFlags Flags)
    {
        int current;
        int next;
        do
        {
            current = ConnectionFlags;
            next = current ^ (int)Flags;
        } while (Interlocked.CompareExchange(ref ConnectionFlags, next, current) != current);
    }

    protected virtual NetaLogger CreateLogger(string NetModeString)
    {
        return new NetaLogger($"{NetModeString} - {ToString()}");
    }

    public PooledNetByteWriter RentWriter(int MinBytes = 512)
    {
        return PooledNetByteWriter.Rent(MinBytes);
    }


    public void InitLocalConnection(NetaDriver Driver, EndPoint RemoteEndPoint, NetaConnectionMode Mode, PacketStatistics? PktStats = null, int RecBufferSize = 64 * 1024 * 1024, int SendBufferSize = 64 * 1024 * 1024)
    {
        InitLocalConnection(Driver, new EndPointKey((IPEndPoint)RemoteEndPoint), Mode, PktStats, RecBufferSize, SendBufferSize);
    }
    public void InitLocalConnection(NetaDriver Driver, EndPointKey RemoteEndPointKey, NetaConnectionMode Mode, PacketStatistics? PktStats = null, int RecBufferSize = 64 * 1024 * 1024, int SendBufferSize = 64 * 1024 * 1024)
    {
        if (Begun) return;
        Begun = true;

        NetModeString = "Client";
        Logger = CreateLogger(NetModeString);
        this.Driver = Driver;
        this.Mode = Mode;
        this.RemoteEndPointKey = RemoteEndPointKey;
        this.RemoteEndPoint = RemoteEndPointKey.ToEndPoint();
        if (PktStats == null) PktStats = new PacketStatistics();
        PacketStats = PktStats;

        InitLocal_TransportLayer(RecBufferSize, SendBufferSize);

        PackageMap = new ConnectionPackageMap(this);
        Channels[0] = Channel = CreateChannel(0);

        StartLocal();
    }

    internal void InitRemoteConnection(NetaDriver Driver, NetaSocket Socket, EndPointKey RemoteEndPointKey, NetaConnectionMode Mode, PacketStatistics? PktStats = null)
    {
        if (Begun) return;
        Begun = true;
        this.Driver = Driver;
        this.Socket = Socket;
        NetModeString = "Server";
        this.RemoteEndPointKey = RemoteEndPointKey;
        RemoteEndPoint = RemoteEndPointKey.ToEndPoint();

        Logger = CreateLogger(NetModeString);

        ConnectionSetFlags(NetaConnectionFlags.Server);
        this.Mode = Mode;

        if (PktStats == null) PktStats = new PacketStatistics();
        PacketStats = PktStats;
        InitRemote_TransportLayer();

        PackageMap = new ConnectionPackageMap(this);
        Channels[0] = Channel = CreateChannel(0);

        StartRemote();
    }

    protected void StartLocal_Auto()
    {
        throw new NotImplementedException();
    }

    protected void StartLocal_AutoDeferred()
    {
        if (TickActionId != 0) return;
        //DeferredReceiveTask = Task.Run(ReceiveLoopAsync);
        TickActionId = ParallelTickManager.Register(Tick_Local);
    }


    protected void StartRemote_Auto()
    {
        throw new NotImplementedException();
    }
    protected void StartRemote_AutoDeferred()
    {
        //if (TickActionId != 0) return;
        //TickActionId = AutoParallelTickManager.Register(Tick_Remote);
    }



    protected void StartRemote()
    {
        if (TickActionId != 0) return;

        switch (Mode)
        {
            case NetaConnectionMode.Auto:
                StartRemote_Auto(); break;
            case NetaConnectionMode.AutoDeferred:
                StartRemote_AutoDeferred(); break;
            case NetaConnectionMode.Manual:
                break;
            default:
                throw new InvalidOperationException();
        }

        //TickActionId = AutoParallelTickManager.Register(ServerTick);
    }

    protected void StartLocal()
    {
        if (TickActionId != 0) return;

        switch (Mode)
        {
            case NetaConnectionMode.Auto:
                StartLocal_Auto(); break;
            case NetaConnectionMode.AutoDeferred:
                StartLocal_AutoDeferred(); break;
            case NetaConnectionMode.Manual:
                break;
            default:
                throw new InvalidOperationException();
        }
    }




    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected PooledOutPacket CreatePacket(EConnectionPacketMessage ConMessage) => CreatePacket<NetaConnection>(ConMessage);

    protected PooledOutPacket CreatePacket<T>(EConnectionPacketMessage ConMessage)
    {
        var Packet = CreatePacket<T>(EProtocolMessage.Unreliable);
        Packet.Serialize(ConMessage, 1);
        return Packet;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public PooledOutPacket CreateReliablePacket(EConnectionPacketMessage ConMessage) => CreateReliablePacket<NetaConnection>(ConMessage);
    public PooledOutPacket CreateReliablePacket<T>(EConnectionPacketMessage ConMessage)
    {
        var Packet = CreateReliablePacket<T>(EProtocolMessage.Reliable);

        Packet.Serialize(ConMessage, 1);

        return Packet;
    }


    private void OnReliablePacket(PooledInPacket Packet)
    {
        var Msg = Packet.Serialize<EConnectionPacketMessage>(1);
        var ConnFlags = Packet.Serialize<EConnectionPacketFlags>();

        //(Packet.Flags & EPacketFlags.HasAcks) != 0
        if ((ConnFlags & EConnectionPacketFlags.HasObjectAcks) != 0) PackageMap.ImportObjectAcks(Packet);
        if ((ConnFlags & EConnectionPacketFlags.HasObjectCreations) != 0) PackageMap.ImportObjectCreations(Packet);
        if ((ConnFlags & EConnectionPacketFlags.HasMappings) != 0) PackageMap.ImportMappings(Packet);

        switch (Msg)
        {
            case EConnectionPacketMessage.Control:
                throw new NotImplementedException();
            case EConnectionPacketMessage.Connect:
                ConnectReceiveQueue.Writer.TryWrite(Packet); break;
            case EConnectionPacketMessage.Remote:
                throw new NotImplementedException();
            case EConnectionPacketMessage.Channel:
                Process_InReliablePacket_Channel(Packet); break;
            case EConnectionPacketMessage.ChannelOpen:
                Process_InReliablePacket_OpenChannel(Packet); break;
            case EConnectionPacketMessage.ChannelClose:
                throw new NotImplementedException();
#if !RELEASE
            default:
                throw new InvalidPacketException($"Invalid connection packet message. PktId {Packet.Id} Msg: {(int)Msg}");
#endif
        }
    }

    private void OnUnreliablePacket(PooledInPacket Packet)
    {
        var Msg = Packet.Serialize<EConnectionPacketMessage>(1);

        switch (Msg)
        {
            case EConnectionPacketMessage.Control:
                throw new NotImplementedException();
            case EConnectionPacketMessage.Connect:
                throw new InvalidOperationException("Connect packet cannot be unreliable");
            case EConnectionPacketMessage.Remote:
                throw new NotImplementedException();
            case EConnectionPacketMessage.Channel:
                Process_InUnreliablePacket_Channel(Packet);
                break;
            case EConnectionPacketMessage.ChannelOpen:
                throw new InvalidPacketException($"Invalid connection packet message. PktId {Packet.Id} Msg: {(int)Msg}");
            case EConnectionPacketMessage.ChannelClose:
                throw new InvalidPacketException($"Invalid connection packet message. PktId {Packet.Id} Msg: {(int)Msg}");
#if !RELEASE
            default:
                throw new InvalidPacketException($"Invalid connection packet message. PktId {Packet.Id} Msg: {(int)Msg}");
#endif
        }
    }

    protected virtual NetaChannel CreateChannel(byte ChanIndex)
    {
        var NewChannel = new NetaChannel(this, ChanIndex);
        UInt32 ChannId = (UInt32)ChanIndex + 2;
        NewChannel.ISetNetworkId(ChannId);
        PackageMap.MapChannel(NewChannel, ChannId);
        return NewChannel;
    }










    class NetConnection_Process_InReliablePacket { }
    private void Process_InReliablePacket_Channel(PooledInPacket Packet)
    {
        var NumBunches = Packet.Serialize<Neta_BunchCountType>();
        for (int Index = 0; Index < NumBunches; Index++)
        {
            var BunchBytes = Packet.Serialize<Neta_BunchSizeType>();
            var NewBunch = InBunch.Rent<NetConnection_Process_InReliablePacket>(PackageMap, Packet, BunchBytes);

            var TargetChannel = Channels[NewBunch.ChannelIndex];

            if (TargetChannel != null)
            {
                Channels[NewBunch.ChannelIndex]!.Receive_InBunch(NewBunch);
            }
            else
            {
                var ChanIndex = Packet.Serialize<UInt16>();
                var NewChannel = (NetaChannel)Packet.SerializeObject()!;
                Channels[ChanIndex] = NewChannel;
            }
        }

        Packet.Return();
    }

    private void Process_InReliablePacket_OpenChannel(PooledInPacket Packet)
    {
        var NumBunches = Packet.Serialize<Neta_BunchCountType>();
        for (int Index = 0; Index < NumBunches; Index++)
        {
            var BunchBytes = Packet.Serialize<Neta_BunchSizeType>();
            var NewBunch = InBunch.Rent<NetConnection_Process_InReliablePacket>(PackageMap, Packet, BunchBytes);




        }

        Packet.Return();
    }

    class NetConnection_Process_InUnreliablePacket { }
    private void Process_InUnreliablePacket_Channel(PooledInPacket Packet)
    {
        var NumBunches = Packet.Serialize<Neta_BunchCountType>();
        for (int Index = 0; Index < NumBunches; Index++)
        {
            var BunchBytes = Packet.Serialize<Neta_BunchSizeType>();
            var NewBunch = InBunch.Rent<NetConnection_Process_InUnreliablePacket>(PackageMap, Packet, BunchBytes);
            Channels[NewBunch.ChannelIndex]!.Receive_InBunch(NewBunch);
        }

        Packet.Return();
    }



    public void SendBunch(OutBunch Bunch)
    {
        if (ConnectionHasFlags(NetaConnectionFlags.Shutdown))
        {
            Bunch.Return();
        }

        PacketStats.IncrementAppOut();
        OutBunchQueue.Add(Bunch);
    }

    public void SendBunchImmediate(OutBunch Bunch)
    {
        Bunch.Return();
        throw new NotImplementedException();
    }


    void DequeueBunches(List<OutBunch> ReliableBunches, List<OutBunch> UnreliableBunches)
    {
        while (OutBunchQueue.Take(out var Bunch))
        {
            if ((Bunch.Flags & EBunchFlags.Reliable) != 0)
            {
                ReliableBunches.Add(Bunch);
            }
            else
            {
                UnreliableBunches.Add(Bunch);
            }
        }
    }


    class NetConnection_ProcessUnreliableBunches_1 { }
    class NetConnection_ProcessUnreliableBunches_2 { }
    void ProcessUnreliableBunches(List<OutBunch> UnreliableBunches, List<PooledOutPacket> UnreliablePackets)
    {
        PooledOutPacket? UnreliablePacket = null;
        int TotalBytes = 0;
        int NumBunchesPos = 0;
        Neta_BunchCountType NumBunches = 0;

        for (int i = UnreliableBunches.Count - 1; i >= 0; i--)
        {
            var Bunch = UnreliableBunches[i];
            if (Bunch.Pos > NetaConsts.BunchMaxSizeBytes)
            {
                Logger.LogError($"Unreliable Bunch cannot be larger than {NetaConsts.BunchMaxSizeBytes} [NetworkConsts.BunchMaxSizeBytes], BunchSize {Bunch.Pos} Bytes");

                Bunch.Return();
                if (UnreliablePacket != null) UnreliablePacket.Return();
                Shutdown();
                break;
            }

            if (UnreliablePacket != null)
            {
                TotalBytes += Bunch.Pos + 32;
            }
            else
            {
                NumBunches = 0;
                UnreliablePacket = CreatePacket<NetConnection_ProcessUnreliableBunches_1>(EConnectionPacketMessage.Channel);
                NumBunchesPos = UnreliablePacket.Pos;
                UnreliablePacket.Serialize(NumBunches);
                TotalBytes = UnreliablePacket.Pos + Bunch.Pos + 32;

                if (TotalBytes > NetaConsts.BufferMaxSizeBytes)
                {
                    Bunch.Return();
                    UnreliablePackets.Add(UnreliablePacket);
                    UnreliablePacket = null;
                    continue;
                }
            }

            if (TotalBytes <= NetaConsts.BufferMaxSizeBytes)
            {
                NumBunches++;
                UnreliablePacket.Serialize((Neta_BunchSizeType)Bunch.Pos);
                UnreliablePacket.Serialize(Bunch);
                Bunch.Return();

                if (i == 0)
                {
                    var OldPos = UnreliablePacket.Pos;
                    UnreliablePacket.SetPos(NumBunchesPos);
                    UnreliablePacket.Serialize(NumBunches);
                    UnreliablePacket.SetPos(OldPos);
                    UnreliablePackets.Add(UnreliablePacket);
                }
            }
            else
            {
                var OldPos = UnreliablePacket.Pos;
                UnreliablePacket.SetPos(NumBunchesPos);
                UnreliablePacket.Serialize(NumBunches);
                UnreliablePacket.SetPos(OldPos);
                UnreliablePackets.Add(UnreliablePacket);

                NumBunches = 0;
                UnreliablePacket = CreatePacket<NetConnection_ProcessUnreliableBunches_2>(EConnectionPacketMessage.Channel);
                NumBunchesPos = UnreliablePacket.Pos;
                UnreliablePacket.Serialize(NumBunches);
                TotalBytes = UnreliablePacket.Pos + Bunch.Pos + 32;

                if (TotalBytes > NetaConsts.BufferMaxSizeBytes)
                {
                    Bunch.Return();
                    UnreliablePackets.Add(UnreliablePacket);
                    UnreliablePacket = null;
                    continue;
                }

                NumBunches++;
                UnreliablePacket.Serialize((Neta_BunchSizeType)Bunch.Pos);
                UnreliablePacket.Serialize(Bunch);
                Bunch.Return();

                if (i == 0)
                {
                    OldPos = UnreliablePacket.Pos;
                    UnreliablePacket.SetPos(NumBunchesPos);
                    UnreliablePacket.Serialize(NumBunches);
                    UnreliablePacket.SetPos(OldPos);
                    UnreliablePackets.Add(UnreliablePacket);
                }
            }
        }
    }












    void Disconnect()
    {
        if (ConnectionHasFlags(NetaConnectionFlags.Shutdown))
        {
            return;
        }

        ConnectionSetFlags(NetaConnectionFlags.Shutdown);

        if (ConnectionHasFlags(NetaConnectionFlags.Pending))
        {
            ConnectionSetFlags(NetaConnectionFlags.Pending);
        }
        else
        {
            ConnectionClearFlags(NetaConnectionFlags.Connected);

            OnConnectionClosed?.Invoke(this);
            Server?.ConnectionClosed(this);
        }

        Shutdown();
    }
    public virtual void Shutdown()
    {
        if (ConnectionHasFlags(NetaConnectionFlags.Shutdown)) return;
        ConnectionSetFlags(NetaConnectionFlags.Shutdown);
        Disconnect();
        if (TickActionId != 0) ParallelTickManager.Unregister(TickActionId);
        TickActionId = 0;

        Shutdown_TransportLayer();

        foreach (var Chan in Channels) Chan?.Shutdown();
    }

    public virtual async Task WaitForCompletionAsync()
    {
        if (ReceiveTask != null) await ReceiveTask;
        else if (DeferredReceiveTask != null) await DeferredReceiveTask;
    }

    protected virtual void Cleanup()
    {
        while (OutBunchQueue.Take(out var Bunch)) Bunch.Return();
        Cleanup_Transport();
        ConnectionSetFlags(NetaConnectionFlags.Cleaned);
    }
}