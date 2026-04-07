using Astral.Containers;
using Astral.Network.Enums;
using Astral.Network.Tools;
using Astral.Network.Transport;
using Astral.Tick;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Astral.Network.Connections;

public partial class NetaConnection
{
    protected void SendPacket_Remote(OutPacket Packet)
    {
        double SecondsSinceLastReceive = (ParallelTickManager.ThisTickTicks - PacketStats.LastReceiveTicks) / (double)Context.ClockFrequency;

        if (SecondsSinceLastReceive > PacketStatistics.TimeoutSeconds)
        {
            Shutdown();
            return;
        }

        PacketStats.IncrementOutPacket();
        Socket.SendTo(new ArraySegment<byte>(Packet.GetBuffer(), 0, Packet.Pos), RemoteEndPoint);
    }


    protected void SendPacket_Remote(OutPacket Packet, int WorkerIndex)
    {
        double SecondsSinceLastReceive = (ParallelTickManager.ThisTickTicks - PacketStats.LastReceiveTicks) / (double)Context.ClockFrequency;

        if (SecondsSinceLastReceive > PacketStatistics.TimeoutSeconds)
        {
            Shutdown();
            return;
        }

        PacketStats.IncrementOutPacket();
#if LINUX
            Server!.EnqueueOutPacket(Packet, SocketAddr, WorkerIndex);
#else
        Socket.SendTo(new ArraySegment<byte>(Packet.GetBuffer(), 0, Packet.Pos), RemoteEndPoint);
#endif
    }




    protected void HandlePong_Remote(InPacket Packet)
    {
        PacketStats.IncrementInPacket();
    }




    class NetaConnection_Tick_RemoteSends { }
    class NetaConnection_Tick_RemoteAckSends { }
    class NetaSocket_TickRemotePingSend { }



    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Tick_RemoteResends(long NowTicks, int WorkerIndex)
    {
        long ElapsedTicks = NowTicks - LastResendTicks;

        if (ElapsedTicks < (PacketStats.GetRetransmissionTimeoutTicks()) /** (Context.ClockFrequency / 1000.0)*/)
        {
            return;
        }

        LastResendTicks = NowTicks;

        OutPacketWindow.Sweep(NowTicks, PacketStats.GetRetransmissionTimeoutTicks(), (Pkt) =>
        {
            SendPacket_Remote(Pkt, WorkerIndex);
            PacketStats.IncrementRetransmitted();
        });
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Tick_RemoteSends(long NowTicks, int WorkerIndex)
    {
        var ReliablePackets = PooledList<PooledOutPacket>.Rent(64);
        var UnreliablePackets = PooledList<PooledOutPacket>.Rent(64);

        while (SendReliableQueue.Take(out var RelPacket))
        {
            if (RelPacket.Pos > NetaConsts.BufferMaxSizeBytes)
            {
                PktFragmentationHandler.Process(RelPacket, ReliablePackets, NowTicks);
            }
            else
            {
                RelPacket.FinalizeReliablePacket(NowTicks);
                ReliablePackets.Add(RelPacket);
            }
        }

        while (SendQueue.Take(out var UnrelPacket)) UnreliablePackets.Add(UnrelPacket);

        var ReliableBunches = PooledList<OutBunch>.Rent(64);
        var UnreliableBunches = PooledList<OutBunch>.Rent(64);

        DequeueBunches(ReliableBunches, UnreliableBunches);

        if (ReliableBunches.Count > 0)
        {
            var RelPacket = CreateReliablePacket<NetaConnection_Tick_RemoteSends>(EConnectionPacketMessage.Channel);

            EConnectionPacketFlags PktFlags = EConnectionPacketFlags.None;
            if (PackageMap.HasObjectCreationExports()) PktFlags |= EConnectionPacketFlags.HasObjectCreations; // Server -> Client
            if (PackageMap.HasObjectMappingExports()) PktFlags |= EConnectionPacketFlags.HasMappings; // Server -> Client
            if (PackageMap.HasObjectAckExports()) PktFlags |= EConnectionPacketFlags.HasObjectAcks; // Client -> Server

            RelPacket.Serialize(PktFlags);

            if (PackageMap.HasObjectCreationExports()) PackageMap.ExportObjectCreation(RelPacket); // Server -> Client
            if (PackageMap.HasObjectMappingExports()) PackageMap.ExportMappings(RelPacket); // Server -> Client
            if (PackageMap.HasObjectAckExports()) PackageMap.ExportObjectAcks(RelPacket); // Client -> Server

            RelPacket.Serialize((Neta_BunchCountType)ReliableBunches.Count);
            foreach (var Bunch in ReliableBunches)
            {
                RelPacket.Serialize((Neta_BunchSizeType)Bunch.Pos);
                RelPacket.Serialize(Bunch);
                Bunch.Return();
            }

            if (RelPacket.Pos > NetaConsts.BufferMaxSizeBytes)
            {
                PktFragmentationHandler.Process(RelPacket, ReliablePackets, NowTicks);
            }
            else
            {
                RelPacket.FinalizeReliablePacket(NowTicks);
                ReliablePackets.Add(RelPacket);
            }
        }
        ReliableBunches.Return();

        if (UnreliableBunches.Count > 0) ProcessUnreliableBunches(UnreliableBunches, UnreliablePackets);
        UnreliableBunches.Return();

        if (ReliablePackets.Count > 0)
        {
            bool Choked = false;
            var DeadlineTicks = NowTicks + PacketStats.GetRetransmissionTimeoutTicks();
            foreach (var RelPacket in ReliablePackets)
            {
                Choked |= !OutPacketWindow.AddPending(RelPacket, DeadlineTicks);
                SendPacket_Remote(RelPacket, WorkerIndex);
            }
            if (Choked)
            {
                Shutdown();
            }
        }

        ReliablePackets.Return();

        foreach (var UnrelPacket in UnreliablePackets)
        {
            UnrelPacket.FinalizePacket();
            SendPacket_Remote(UnrelPacket, WorkerIndex);
            UnrelPacket.Return();
        }
        UnreliablePackets.Return();
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Tick_RemoteAckSends(long TicksNow, int WorkerIndex)
    {
        PooledList<FlightAck> AcksList = PooledList<FlightAck>.Rent(32);
        while (OutgoingAcksQueue.TryPeek(out var PendingAck))
        {
            if (PendingAck.DueTicks < TicksNow) break;

            OutgoingAcksQueue.TryDequeue(out PendingAck);
            AcksList.Add(new FlightAck(TicksNow, PendingAck));
        }

        while (AcksList.Count > 0)
        {
            var Packet = PooledOutPacket.Rent<NetaConnection_Tick_RemoteAckSends>(NextPacketId, EProtocolMessage.None);
            Packet.Flags |= EPacketFlags.HasAcks | EPacketFlags.HasAcksOnly;

            if (AcksList.Count > NetaConsts.AckPerPacketMaxCount)
            {
                Packet.Serialize(CollectionsMarshal.AsSpan(AcksList)[..NetaConsts.AckPerPacketMaxCount]);
                AcksList.RemoveRange(0, NetaConsts.AckPerPacketMaxCount);
            }
            else
            {
                Packet.Serialize(AcksList);
                AcksList.Clear();
            }

            Packet.FinalizePacket();
            SendPacket_Remote(Packet, WorkerIndex);
            Packet.Return();
        }
        AcksList.Return();
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Tick_RemotePingSend(long NowTicks, int WorkerIndex)
    {
        //long ElapsedTicks = NowTicks - LastPingTicks;
        //
        //if (/*ElapsedTicks < PacketStats.LastSendTicks || */ElapsedTicks < PacketStatistics.PingsSendTicks)
        //{
        //	return;
        //}
        //
        //LastPingTicks = NowTicks;
        //
        //var Packet = CreateReliablePacket<NetaSocket_TickRemotePingSend>(EProtocolMessage.Ping);
        //try
        //{
        //	PacketStats.IncrementOutPacket();
        //
        //	Packet.FinalizeReliablePacket(NowTicks);
        //	SendPacket_Remote(Packet, WorkerIndex);
        //}
        //finally
        //{
        //	Packet.Return();
        //}
    }


    private Int32 InTickRemote = 0;
    [Conditional("DEBUG")]
    [Conditional("DEVELOPMENT")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void EnterTick_Remote()
    {
        Int32 OldValue = Interlocked.CompareExchange(ref InTickRemote, 1, 0);
        NetGuard.DebugAssert(OldValue == 0, "Re-entrant Tick_Remote!");
    }
    [Conditional("DEBUG")]
    [Conditional("DEVELOPMENT")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void ExitTick_Remote()
    {
        Int32 OldValue = Interlocked.CompareExchange(ref InTickRemote, 0, 1);
        NetGuard.DebugAssert(OldValue == 1, "Exiting Tick_Remote that wasn't entered!");
    }

    public void Tick_Remote(int WorkerIndex)
    {
        if (ConnectionHasFlags(NetaConnectionFlags.Shutdown))
        {
            if (ConnectionHasFlags(NetaConnectionFlags.Cleaned)) return;
            Cleanup();
            return;
        }
        EnterTick_Remote();
        var TicksNow = ParallelTickManager.ThisTickTicks;
        try
        {
            Tick_RemoteResends(TicksNow, WorkerIndex);
            Tick_RemoteSends(TicksNow, WorkerIndex);
            Tick_RemoteAckSends(TicksNow, WorkerIndex);
            Tick_RemotePingSend(TicksNow, WorkerIndex);
        }
        catch (Exception Ex)
        {
            NetGuard.DebugFail(Ex.ToString());
            OnException(Ex, "NetaConnection - ServerTick");
        }
        finally { ExitTick_Remote(); }
    }
}