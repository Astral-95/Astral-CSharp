using Astral.Containers;
using Astral.Logging;
using Astral.Network.Enums;
using Astral.Network.Sockets;
using Astral.Network.Toolkit;
using Astral.Network.Tools;
using Astral.Network.Transport;
using Astral.Tick;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Astral.Network.Servers.NetaServer;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Astral.Network.Connections;

public partial class NetaConnection
{
#if LINUX
    bool RevMultiMessageInitialized { get; set; } = false;
    uint RecvMiltiMessageBatchSize { get; set; }

    Mmsghdr[] RecvMsgVec;
    IOVector[] RecvIoVecs;
    SocketAddrStorage[] RecvAddresses;
    PooledInPacket[] RecvMultiMessagePackets;


    unsafe Mmsghdr* RecvMsgVecPtr;
    unsafe IOVector* RecvIoVecsPtr;
    unsafe SocketAddrStorage* RecvAddressesPtr;

    nint MsgvecPtr;
    nint IovecsPtr;
    int ScratchCapacity;

    int SendMultiMessageOutgoingQueueCount = 0;
    PendingOutPacket[] SendMultiMessageOutgoingQueue = new PendingOutPacket[16]; 
#endif


    ConcurrentFastQueue<PooledInPacket> IncomingQueue = new ConcurrentFastQueue<PooledInPacket>();

    protected void SendPacket_Local(OutPacket Packet)
    {
        double SecondsSinceLastReceive = (ParallelTickManager.ThisTickTicks - PacketStats.LastReceiveTicks) / (double)Context.ClockFrequency;

        if (SecondsSinceLastReceive > PacketStatistics.TimeoutSeconds)
        {
            Shutdown();
            return;
        }

        PacketStats.IncrementOutPacket();

#if !LINUX
        Socket.Send(new ArraySegment<byte>(Packet.GetBuffer(), 0, Packet.Pos));
#else   
        if (SendMultiMessageOutgoingQueueCount >= SendMultiMessageOutgoingQueue.Length)
        {
            int NewSize = SendMultiMessageOutgoingQueue.Length == 0 ? 64 : SendMultiMessageOutgoingQueue.Length * 2;
            Array.Resize(ref SendMultiMessageOutgoingQueue, NewSize);
        }

        ref PendingOutPacket Pending = ref SendMultiMessageOutgoingQueue[SendMultiMessageOutgoingQueueCount++];

        Packet.GetBuffer().AsSpan(0, Packet.Pos).CopyTo(MemoryMarshal.CreateSpan(ref Pending.Data[0], Packet.Pos));
        Pending.Length = Packet.Pos;
        Pending.Destination = SocketAddr;
#endif
    }

    //void ReliablePacket_Local(PooledOutPacket Packet)
    //{
    //    SendReliableQueue.Enqueue(Packet);
    //}
    //
    //void ReliablePacket_Local(List<PooledOutPacket> Packets)
    //{
    //    foreach (var Packet in Packets)
    //    {
    //        SendReliableQueue.Enqueue(Packet);
    //    }
    //}

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





    class NetaConnection_Tick_LocalReceive_1 { }
    class NetaConnection_Tick_LocalReceive_2 { }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void Tick_LocalReceive()
    {
#if LINUX
        if (!RevMultiMessageInitialized)
        {
            RevMultiMessageInitialized = true;

            // Allocate pinned arrays to ensure pointers remain valid for the life of the object
            RecvMsgVec = GC.AllocateArray<Mmsghdr>((int)RecvMiltiMessageBatchSize, pinned: true);
            RecvIoVecs = GC.AllocateArray<IOVector>((int)RecvMiltiMessageBatchSize, pinned: true);
            RecvAddresses = GC.AllocateArray<SocketAddrStorage>((int)RecvMiltiMessageBatchSize, pinned: true);
            RecvMultiMessagePackets = GC.AllocateArray<PooledInPacket>((int)RecvMiltiMessageBatchSize, pinned: true);

            // Cache the pointers so we don't have to fix them every tick
            RecvMsgVecPtr = (Mmsghdr*)Unsafe.AsPointer(ref RecvMsgVec[0]);
            RecvIoVecsPtr = (IOVector*)Unsafe.AsPointer(ref RecvIoVecs[0]);
            RecvAddressesPtr = (SocketAddrStorage*)Unsafe.AsPointer(ref RecvAddresses[0]);

            for (int i = 0; i < RecvMiltiMessageBatchSize; i++)
            {
                var Pkt = PooledInPacket.Rent<NetaConnection_Tick_LocalReceive_1>();
                RecvMultiMessagePackets[i] = Pkt;
                // Point the IOVector to the specific PacketBuffer index
                RecvIoVecsPtr[i].Base = (IntPtr)Pkt.GetBuffer();
                RecvIoVecsPtr[i].Length = (IntPtr)NetaConsts.BufferMaxSizeBytes;

                // Link the Mmsghdr to the corresponding IOVector and Address slot
                RecvMsgVecPtr[i].msg_hdr.msg_iov = (IntPtr)(&RecvIoVecsPtr[i]);
                RecvMsgVecPtr[i].msg_hdr.msg_iovlen = (IntPtr)1;
                RecvMsgVecPtr[i].msg_hdr.msg_name = (IntPtr)(&RecvAddressesPtr[i]);
                RecvMsgVecPtr[i].msg_hdr.msg_namelen = sizeof(SocketAddrStorage);
            }
        }

        while (true)
        {
            const int MSG_DONTWAIT = 0x40;

            var NumPkts = Socket.Recvmmsg(RecvMsgVecPtr, RecvMiltiMessageBatchSize, MSG_DONTWAIT, null);

            if (NumPkts < 0)
            {
                int err = Marshal.GetLastPInvokeError();
                if (err == 4 || err == 11 || !ConnectionHasFlags(NetaConnectionFlags.Connected)) return; // EINTR or EAGAIN/EWOULDBLOCK or not connected

                Shutdown();
                Logger.LogError($"Tick_LocalReceive: recvmmsg failed with errno {err}");
                return;
            }

            if (NumPkts == 0) return;

            for (int i = 0; i < NumPkts; i++)
            {
                int len = (int)RecvMsgVecPtr[i].msg_len;
                RecvMsgVecPtr[i].msg_hdr.msg_namelen = sizeof(SocketAddrStorage);

                if (len < 1)
                {
                    Shutdown();
                    return;
                }

                var InPacket = RecvMultiMessagePackets[i];
                var Key = new NetaAddress(ref RecvAddressesPtr[i]);

                var NumBytes = Unsafe.ReadUnaligned<Neta_PacketSizeType>(InPacket.GetBuffer());

                if (NumBytes > InPacket.Length)
                {
                    Logger.LogError($"{NetModeString}: Numbytes is larger than buffer length.\n Numbytes: {NumBytes} BuffLen: {InPacket.Length}");
                    Shutdown();
                    return;
                }
                if (NumBytes < 1)
                {
                    Logger.LogError($"{NetModeString}: Numbytes field is less than [1]. Value: {NumBytes}");
                    Shutdown();
                    return;
                }

                InPacket.Num = NumBytes;

                try
                {
                    Dispatch_IncomingPacket(InPacket);
                }
                catch (Exception Ex)
                {
                    if (!ConnectionHasFlags(NetaConnectionFlags.Shutdown))
                    {
                        Logger.LogError($"{NetModeString}: {Ex}");
                    }

                    InPacket.Return();

                    Shutdown();
                }
                finally
                {
                    var NewInPacket = PooledInPacket.Rent<NetaConnection_Tick_LocalReceive_2>();
                    RecvMultiMessagePackets[i] = NewInPacket;
                    RecvIoVecsPtr[i].Base = (IntPtr)NewInPacket.GetBuffer();
                }
            }

            if (NumPkts < RecvMiltiMessageBatchSize) return;
        }
#else
        PooledInPacket? Packet = null;

        try
        {
            while (Socket.Poll(0, SelectMode.SelectRead))
            {
                Packet = PooledInPacket.Rent<NetaConnection_Tick_LocalReceive_1>();

                if (ConnectionHasFlags(NetaConnectionFlags.Shutdown))
                {
                    Packet.Return();
                    return;
                }

                int BytesReceived = Socket.Receive(Packet.AsSpan());

                if (BytesReceived < 1)
                {
                    Packet.Return();
                    Shutdown();
                    break;
                }

                var NumBytes = Unsafe.ReadUnaligned<Neta_PacketSizeType>(Packet.GetBuffer());
#if NETA_DEBUG
                if (NumBytes > Packet.Length)
                {
                    throw new InvalidOperationException($"Numbytes is larger than buffer length.\n Numbytes: {NumBytes} BuffLen: {Packet.Length}");
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
            if (!ConnectionHasFlags(NetaConnectionFlags.Shutdown))
            {
                Logger.LogError($"{NetModeString}: {Ex}");
            }
            
            Shutdown();
        }
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Tick_LocalResends(long NowTicks)
    {
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

        if (!ConnectionHasFlags(NetaConnectionFlags.Connected))
        {
            while (ConnectWriterQueue.TryDequeue(out var ConnectWriter))
            {
                var ConnectPacket = CreateReliablePacket(EProtocolMessage.Connect);

                ConnectPacket.Serialize(NextHandshakePacketId++);
                ConnectPacket.Serialize(ConnectWriter);

                if (ConnectPacket.Pos > NetaConsts.BufferMaxSizeBytes)
                {
                    PktFragmentationHandler.Process(ConnectPacket, ReliablePackets, NowTicks);
                }
                else
                {
                    ConnectPacket.FinalizeReliablePacket(NowTicks);
                    ReliablePackets.Add(ConnectPacket);
                }
            }
        }

        var ReliableBunches = PooledList<OutBunch>.Rent(64);
        var UnreliableBunches = PooledList<OutBunch>.Rent(64);

        DequeueBunches(ReliableBunches, UnreliableBunches);

        if (ReliableBunches.Count > 0)
        {
            var RelPacket = CreateReliablePacket<NetaConnection_Tick_LocalSends>(EConnectionPacketMessage.Channel);

            EConnectionPacketFlags PktFlags = EConnectionPacketFlags.None;
            if (PackageMap.HasObjectAckExports()) PktFlags |= EConnectionPacketFlags.HasObjectAcks;

            RelPacket.Serialize(PktFlags);

            if (PackageMap.HasObjectAckExports()) PackageMap.ExportObjectAcks(RelPacket);

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

#if LINUX
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    unsafe void Tick_LocalFlushSendMultiMessage()
    {
        if (SendMultiMessageOutgoingQueueCount == 0) return;

        if (SendMultiMessageOutgoingQueueCount > ScratchCapacity)
        {
            int newSize = Math.Max(SendMultiMessageOutgoingQueueCount, Math.Max(ScratchCapacity * 2, 64));

            if (MsgvecPtr != 0) NativeMemory.Free((void*)MsgvecPtr);
            if (IovecsPtr != 0) NativeMemory.Free((void*)IovecsPtr);

            MsgvecPtr = (nint)NativeMemory.AllocZeroed((nuint)(newSize * sizeof(Mmsghdr)));
            IovecsPtr = (nint)NativeMemory.AllocZeroed((nuint)(newSize * sizeof(IOVector)));
            ScratchCapacity = newSize;
        }

        try
        {
            Mmsghdr* pMsgVec = (Mmsghdr*)MsgvecPtr;
            IOVector* pIoVecs = (IOVector*)IovecsPtr;

            fixed (PendingOutPacket* pQueue = SendMultiMessageOutgoingQueue)
            {
                for (int i = 0; i < SendMultiMessageOutgoingQueueCount; i++)
                {
                    byte* pPacketData = (byte*)&pQueue[i].Data;
                    void* pPacketAddr = &pQueue[i].Destination;

                    pIoVecs[i].Base = (IntPtr)pPacketData;
                    pIoVecs[i].Length = (IntPtr)pQueue[i].Length;

                    pMsgVec[i].msg_hdr.msg_name = (IntPtr)pPacketAddr;
                    pMsgVec[i].msg_hdr.msg_namelen = SendMultiMessageOutgoingQueue[i].Destination.Len;
                    pMsgVec[i].msg_hdr.msg_iov = (IntPtr)(&pIoVecs[i]);
                    pMsgVec[i].msg_hdr.msg_iovlen = (IntPtr)1;
                    pMsgVec[i].msg_hdr.msg_control = IntPtr.Zero;
                    pMsgVec[i].msg_hdr.msg_controllen = IntPtr.Zero;
                    pMsgVec[i].msg_hdr.msg_flags = 0;
                    pMsgVec[i].msg_len = 0;
                }

                int sent = Socket.SendMultiMessage(pMsgVec, (uint)SendMultiMessageOutgoingQueueCount, 0);

                if (sent < 0)
                {
                    int err = Marshal.GetLastWin32Error();
                    Logger.LogError($"Tick_LocalFlushSendMultiMessage: SendMultiMessage failed: errno {err}");
                }
            }
        }
        finally
        {
            SendMultiMessageOutgoingQueueCount = 0;
        }
    } 
#endif


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

#if LINUX
            for (int i = 0; i < RecvMiltiMessageBatchSize; i++)
            {
                RecvMultiMessagePackets[i].Return();
            }
#endif
            return;
        }
        EnterTick_Local();
        var TicksNow = ParallelTickManager.ThisTickTicks;
        try
        {
            Tick_LocalReceive();
            Tick_LocalResends(TicksNow);
            Tick_LocalSends(TicksNow);
            Tick_LocalAckSends(TicksNow);
#if LINUX
            Tick_LocalFlushSendMultiMessage();
#endif
        }
        catch (Exception Ex)
        {
            OnException(Ex, "NetaConnection - ClientTick");
        }
        finally { ExitTick_Local(); }
    }
}