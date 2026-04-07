using Astral.Containers;
using Astral.Network.Enums;
using Astral.Network.Tools;
using Astral.Network.Transport;
using Astral.Tick;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Astral.Network.Connections;

public partial class NetaConnection
{
    ConcurrentStore<PooledInPacket> IncomingQueue = new ConcurrentStore<PooledInPacket>();

    protected void SendPacket_Local(OutPacket Packet)
    {
        double SecondsSinceLastReceive = (ParallelTickManager.ThisTickTicks - PacketStats.LastReceiveTicks) / (double)Context.ClockFrequency;

        if (SecondsSinceLastReceive > PacketStatistics.TimeoutSeconds)
        {
            Shutdown();
            return;
        }

        PacketStats.IncrementOutPacket();
        Socket.Send(new ArraySegment<byte>(Packet.GetBuffer(), 0, Packet.Pos));
    }

    void ReliablePacket_Local(PooledOutPacket Packet)
    {
        SendReliableQueue.Add(Packet);
    }

    void ReliablePacket_Local(List<PooledOutPacket> Packets)
    {
        foreach (var Packet in Packets)
        {
            SendReliableQueue.Add(Packet);
        }
    }

    class NetaSocket_HandlePing_Local { }
    void HandlePing_Local(InPacket Packet)
    {
        var PacketOut = CreatePacket<NetaSocket_HandlePing_Local>(EProtocolMessage.Pong);

        //PacketStats.IncrementPongOut();
        //Driver.PacketStats.IncrementPongOut();
        //
        //PacketOut.FinalizePacket();
        //SendPacket(PacketOut);
        try
        {
            PacketStats.IncrementOutPacket();

            PacketOut.FinalizePacket();
            SendPacket_Local(PacketOut);
        }
        catch { }
        finally { PacketOut.Return(); }
    }





    class NetaSocket_PostReceive { }
    void PostReceive(SocketAsyncEventArgs SocketArgs)
    {
        if (ConnectionHasFlags(NetaConnectionFlags.Shutdown)) return;

        try
        {
            var Packet = PooledInPacket.Rent<NetaSocket_PostReceive>();

            SocketArgs.SetBuffer(Packet.GetBuffer());
            SocketArgs.UserToken = Packet;

            if (!Socket.ReceiveFromAsync(SocketArgs)) OnReceiveCompleted(null, SocketArgs);
        }
        catch (Exception Ex)
        {
            NetGuard.Fail($"NetaSocket - PostReceive Exception: {Ex}");
        }
    }

    class NetaSocket_OnReceiveCompleted { }
    void OnReceiveCompleted(object? sender, SocketAsyncEventArgs SocketArgs)
    {
        PooledInPacket? Packet = (PooledInPacket)SocketArgs.UserToken!;
        try
        {
            int MaxInline = 0;
            while (true)
            {

                if (SocketArgs.BytesTransferred < 1 || SocketArgs.SocketError != SocketError.Success)
                {
                    Packet.Return();
                    Shutdown();
                    return;
                }

                //IncomingQueue.Add((Packet, new EndPointKey((IPEndPoint)SocketArgs.RemoteEndPoint!)));

                Packet = PooledInPacket.Rent<NetaSocket_OnReceiveCompleted>();

                SocketArgs.SetBuffer(Packet.GetBuffer());
                SocketArgs.UserToken = Packet;

                if (Socket.ReceiveFromAsync(SocketArgs))
                {
                    break;
                }
                else
                {
                    if (MaxInline > 128)
                    {
                        Logger.LogWarning("OnReceiveCompleted: SAEA inline loop overload. Switching threads.");
                        ThreadPool.QueueUserWorkItem(_ => { OnReceiveCompleted(null, SocketArgs); });
                        return;
                    }

                    MaxInline++;
                    continue;
                }

            }
        }
        catch (Exception Ex)
        {
            try { Packet.Return(); } catch (Exception PktEx) { Logger.LogCritical(new AggregateException(Ex, PktEx).ToString()); }

            if (Ex is not ObjectDisposedException && Ex is not OperationCanceledException)
            {
                Logger.LogCritical($"PostReceive Exception: {Ex}");
            }

            Shutdown();
            return;
        }
    }


    class NetaConnection_ReceiveLoopAsync_1 { }
    class NetaConnection_ReceiveLoopAsync_2 { }
    class NetaConnection_ReceiveLoopAsync_3 { }
    async Task ReceiveLoopAsync()
    {
        var Packet = PooledInPacket.Rent<NetaConnection_ReceiveLoopAsync_1>();
        Packet.Return();
        while (!ConnectionHasFlags(NetaConnectionFlags.Shutdown))
        {
            Packet = PooledInPacket.Rent<NetaConnection_ReceiveLoopAsync_2>();
            try
            {
                // TODO: handle receiving a buffer larger than Packet.Buffer more cleanly
                SocketReceiveFromResult Result = await Socket.ReceiveFromAsync(Packet.GetBuffer(), ReceiveEndPoint);
                if (ConnectionHasFlags(NetaConnectionFlags.Shutdown)) break;

                if (!RemoteEndPoint.Equals(Result.RemoteEndPoint)) continue;

                if (Result.ReceivedBytes < 1) break;

                IncomingQueue.Add(Packet);

                while (Socket.Available > 0)
                {
                    Packet = PooledInPacket.Rent<NetaConnection_ReceiveLoopAsync_3>();

                    int BytesReceived = Socket.ReceiveFrom(Packet.GetBuffer(), ref ReceiveEndPoint);

                    if (!RemoteEndPoint.Equals(ReceiveEndPoint)) continue;

                    if (BytesReceived < 1) break;

                    IncomingQueue.Add(Packet);
                }
            }
            catch (Exception Ex)
            {
                OnException(Ex, "NetaConnection.ReceiveLoopAsync");
                Packet.TryReturn();
            }
        }
        Packet.TryReturn();
        Shutdown();
    }






    class NetaConnection_Tick_LocalReceive { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Tick_LocalReceive()
    {
        PooledInPacket? Packet = null;

        try
        {
            while (Socket.Available > 0)
            {
                Packet = PooledInPacket.Rent<NetaConnection_Tick_LocalReceive>();

                int BytesReceived = Socket.Receive(Packet.GetBuffer());

                if (BytesReceived < 1) break;

                var NumBytes = MemoryMarshal.Read<Neta_PacketSizeType>(Packet.GetBuffer());
#if NETA_DEBUG
                if (NumBytes > Packet.GetBuffer().Length)
                {
                    throw new InvalidOperationException($"Numbytes is larger than buffer length.\n Numbytes: {NumBytes} BuffLen: {Packet.GetBuffer().Length}");
                }
                if (NumBytes < 1)
                {
                    throw new InvalidOperationException($"Numbytes field is less than [1]. Value: {NumBytes}");
                }
#endif
                Packet.Num = NumBytes;

                Dispatch_IncomingPacket(Packet);
            }
        }
        catch (Exception Ex)
        {
            Packet?.TryReturn();
            Logger.LogError($"{NetModeString}: {Ex}");
            Shutdown();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Tick_LocalResends(long NowTicks)
    {
        long ElapsedTicks = NowTicks - LastResendTicks;

        if (ElapsedTicks < (PacketStats.GetRetransmissionTimeoutTicks()) /** (Context.ClockFrequency / 1000.0)*/)
        {
            return;
        }

        LastResendTicks = NowTicks;

        OutPacketWindow.Sweep(NowTicks, PacketStats.GetRetransmissionTimeoutTicks(), (Pkt) =>
        {
            SendPacket_Local(Pkt);
            PacketStats.IncrementRetransmitted();
        });
    }

    class NetaConnection_Tick_LocalSends { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Tick_LocalSends(long NowTicks)
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
            var RelPacket = CreateReliablePacket<NetaConnection_Tick_LocalSends>(EConnectionPacketMessage.Channel);

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
            var DeadlineTicks = NowTicks + PacketStats.GetRetransmissionTimeoutTicks();
            foreach (var RelPacket in ReliablePackets)
            {
                OutPacketWindow.AddPending(RelPacket, DeadlineTicks);
                SendPacket_Local(RelPacket);
            }
        }
        ReliablePackets.Return();

        foreach (var UnrelPacket in UnreliablePackets)
        {
            UnrelPacket.FinalizePacket();
            SendPacket_Local(UnrelPacket);
            UnrelPacket.Return();
        }
        UnreliablePackets.Return();
    }

    class NetaConnection_Tick_LocalAckSends { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Tick_LocalAckSends(long TicksNow)
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
            var Packet = PooledOutPacket.Rent<NetaConnection_Tick_LocalAckSends>(NextPacketId, EProtocolMessage.None);
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
            SendPacket_Local(Packet);
            Packet.Return();
        }
        AcksList.Return();
    }


    private Int32 InTickLocal = 0;
    [Conditional("DEBUG")]
    [Conditional("DEVELOPMENT")]
    void EnterTick_Local()
    {
        Int32 OldValue = Interlocked.CompareExchange(ref InTickLocal, 1, 0);
        NetGuard.DebugAssert(OldValue == 0, "Re-entrant Tick_Local!");
    }
    [Conditional("DEBUG")]
    [Conditional("DEVELOPMENT")]
    void ExitTick_Local()
    {
        Int32 OldValue = Interlocked.CompareExchange(ref InTickLocal, 0, 1);
        NetGuard.DebugAssert(OldValue == 1, "Exiting Tick_Local that wasn't entered!");
    }

    public void Tick_Local()
    {
        if (ConnectionHasFlags(NetaConnectionFlags.Shutdown))
        {
            if (ConnectionHasFlags(NetaConnectionFlags.Cleaned)) return;
            Cleanup();
            return;
        }
        EnterTick_Local();
        var TicksNow = ParallelTickManager.ThisTickTicks;
        //var TicksNow = AutoParallelTickManager.CurrentTicks;
        try
        {
            Tick_LocalReceive();
            Tick_LocalResends(TicksNow);
            //ProcessBunchSends(TicksNow);
            Tick_LocalSends(TicksNow);
            Tick_LocalAckSends(TicksNow);
        }
        catch (Exception Ex)
        {
            NetGuard.DebugFail(Ex.ToString());
            OnException(Ex, "NetaConnection - ClientTick");
        }
        finally { ExitTick_Local(); }
    }
}