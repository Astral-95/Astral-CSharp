using Astral.Containers;
using Astral.Logging;
using Astral.Network.Enums;
using Astral.Network.Interfaces;
using Astral.Network.Serialization;
using Astral.Network.Servers;
using Astral.Network.Sockets;
using Astral.Network.Toolkit;
using Astral.Network.Tools;
using Astral.Network.Transport;
using Astral.Serialization;
using Astral.Tick;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Astral.Network.Connections;

public enum NetaSocketMode : byte
{
    Auto,
    AutoDeferred,
    Manual
}
public partial class NetaConnection : INetworkObject
{
    internal readonly protected struct PendingAck
    {
        public readonly Neta_PacketIdType Id;
        public readonly long DueTicks;
        public readonly long RemoteTicks;
        public readonly long ProcessTicks;
        public PendingAck(Neta_PacketIdType Id, long DueTicks, long RemoteTicks, long ProcessTicks)
        {
            this.Id = Id;
            this.DueTicks = DueTicks;
            this.RemoteTicks = RemoteTicks;
            this.ProcessTicks = ProcessTicks;
        }
    }

    internal protected struct FlightAck
    {
        public const int SizeBytes = 24;
        public Neta_PacketIdType Id;
        public long Ticks;
        public long ProcessMilliseconds;
        public FlightAck(long TicksNow, PendingAck PendingAck)
        {
            Id = PendingAck.Id;
            Ticks = PendingAck.RemoteTicks;
            ProcessMilliseconds = (TicksNow - PendingAck.ProcessTicks) * 1000 / Context.ClockFrequency;
        }
    }

   
#if LINUX
    internal SocketAddrStorage SocketAddr = default; 
#endif
    protected InPacketWindow PacketWindow = new();
    protected OutPacketWindow OutPacketWindow = new();
    public PacketStatistics PacketStats { get; protected set; }

    internal int InternalNextPacketId = 0;
    internal protected Neta_PacketIdType NextPacketId { get => (Neta_PacketIdType)Interlocked.Increment(ref InternalNextPacketId); }

    // Acks ----------------------------------------------------
    internal protected Queue<PendingAck> OutgoingAcksQueue = new();
    // Acks ----------------------------------------------------


    protected System.Threading.Channels.Channel<ByteReader> ConnectReceiveQueue = System.Threading.Channels.Channel.CreateUnbounded<ByteReader>();

    ConcurrentFastQueue<ByteWriter> ConnectWriterQueue { get; set; } = new();


    public long LastPingTicks { get; set; } = 0;


    void Init_TransportRemote()
    {
        Init_FragmentationHandler();

#if LINUX
        SocketAddr = new SocketAddrStorage(ref NetaRemoteAddr); 
#endif
    }

    void Init_TransportLocal(int RecvMiltiMessageBatchSize, int RecBufferSize, int SendBufferSize)
    {
        Init_FragmentationHandler();

        Socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        Socket.ReceiveBufferSize = RecBufferSize;
        Socket.SendBufferSize = SendBufferSize;
        Socket.Connect(NetaRemoteAddr.GetAddressString(), NetaRemoteAddr.Port);
        NetaLocalAddr = new NetaAddress((IPEndPoint)Socket.LocalEndPoint!);

#if LINUX
        SocketAddr = new SocketAddrStorage(ref NetaRemoteAddr);

        this.RecvMiltiMessageBatchSize = (uint)RecvMiltiMessageBatchSize;
#endif
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal PooledOutPacket CreatePacket(EProtocolMessage Message) => CreatePacket<NetaConnection>(Message);
    internal PooledOutPacket CreatePacket<T>(EProtocolMessage Message)
    {
        PooledOutPacket Packet = PooledOutPacket.Rent<T>(NextPacketId, Message);
        TryPiggyBackAcks(Packet);
        return Packet;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal PooledOutPacket CreateReliablePacket(EProtocolMessage Message) => CreateReliablePacket<NetaConnection>(Message);
    internal PooledOutPacket CreateReliablePacket<T>(EProtocolMessage Message)
    {
        var Pkt = PooledOutPacket.RentReliable<T>(NextPacketId, Message);
        TryPiggyBackAcks(Pkt);
        return Pkt;
    }

    protected void OnException(Exception Ex, string Location)
    {
        if (ConnectionHasFlags(NetaConnectionFlags.Shutdown) || Ex is ObjectDisposedException) { Shutdown(); return; }

        if (Ex is not SocketException SocketEx)
        {
            Logger.LogError($"{Location}: Unhandled exception: {Ex}");
        }
        else
        {
#if CFG_LOG_TRACE || CFG_LOG_WARN || CFG_NET_ERROR
            switch (SocketEx.SocketErrorCode)
            {
                case SocketError.ConnectionReset:
                    Logger.LogWarning($"{Location}: Exception: {Ex.ToString()}"); break;
                case SocketError.OperationAborted:
                    Logger.LogWarning($"{Location}: Exception: {Ex.ToString()}"); break;
                default:
                    Logger.LogError($"{Location}: Unhandled exception: {Ex}"); break;
            }
#endif
        }
        Shutdown();
    }


//    public void Send(PooledOutPacket Packet)
//    {
//#if NETA_DEBUG
//        if (ConnectionHasFlags(NetaConnectionFlags.Shutdown))
//        {
//            Packet.Return();
//            Logger.LogWarning("SendUnreliable(OutPacket): Cannot enqueue ack while shutdown.");
//            return;
//        }
//#endif
//        if (Packet.Pos > NetaConsts.BufferMaxSizeBytes)
//        {
//            Packet.Return();
//            Logger.LogWarning($"SendUnreliable(OutPacket): Sending unreliable packet larger than {NetaConsts.BufferMaxSizeBytes} bytes is not supported.");
//            return;
//        }
//        SendQueue.Enqueue(Packet);
//    }
//
//    public void Send(List<PooledOutPacket> Packets)
//    {
//#if NETA_DEBUG
//        if (ConnectionHasFlags(NetaConnectionFlags.Shutdown))
//        {
//            foreach (var Packet in Packets) Packet.Return();
//            Logger.LogError("SendUnreliable(List<OutPacket>): Cannot enqueue ack while shudown.");
//            return;
//        }
//#endif
//        foreach (var Pkt in Packets)
//        {
//            if (Pkt.Pos > NetaConsts.BufferMaxSizeBytes)
//            {
//                Pkt.Return();
//                Logger.LogError($"SendUnreliable(List<OutPacket>): Sending unreliable packet larger than {NetaConsts.BufferMaxSizeBytes} bytes is not supported.");
//                continue;
//            }
//
//            SendQueue.Enqueue(Pkt);
//        }
//    }









    protected void TryPiggyBackAcks(OutPacket Packet)
    {
        PooledList<FlightAck> AcksList = PooledList<FlightAck>.Rent(NetaConsts.AckPerPacketMaxCount);
        var TicksNow = ParallelTickManager.ThisTickTicks;
        while (OutgoingAcksQueue.TryDequeue(out var OutAck))
        {
            AcksList.Add(new FlightAck(TicksNow, OutAck));

            if (AcksList.Count == NetaConsts.AckPiggybackMaxCount) break;
        }

        if (AcksList.Count < 1)
        {
            AcksList.Return();
            return;
        }

        Packet.Flags |= EPacketFlags.HasAcks;
        Packet.HeaderBytes += FlightAck.SizeBytes * AcksList.Count + NetaConsts.ListCountSizeBytes;
        Packet.Serialize(AcksList);
        AcksList.Return();
    }




    protected Neta_PacketIdType NextHandshakePacketId = 1;
    class NetaConnection_SendHandshakeWriter { }
    protected void SendHandshakeWriter(ByteWriter Writer) => ConnectWriterQueue.Enqueue(new ByteWriter(Writer));

    protected Neta_PacketIdType HandshakePacketExpectedId = 1;
    private readonly ConcurrentDictionary<Neta_PacketIdType, ByteReader> ConnectBuffer = new();
    protected async Task<ByteReader> ReceiveHandshakeReaderAsync(int TimeoutMs, CancellationToken Token = default)
    {
        if (ConnectBuffer.TryRemove(HandshakePacketExpectedId, out var Buffered))
        {
            HandshakePacketExpectedId++;
            return Buffered;
        }

        while (true)
        {
            var Reader = await ConnectReceiveQueue.Reader.ReadAsync(Token);

            var ConnectId = Reader.Serialize<Neta_PacketIdType>();

            if (ConnectId == HandshakePacketExpectedId)
            {
                HandshakePacketExpectedId++;
                return Reader;
            }

            if (ConnectId > HandshakePacketExpectedId)
            {
                ConnectBuffer[ConnectId] = Reader;
                continue;
            }

            Logger.LogCritical("ReceiveConnectReaderAsync: Invalid execution path.");
        }
    }

    class NetaConnection_HandleHandshake_Server { }
    internal protected virtual async Task<bool> HandleHandshake_Server(int TimeoutMs = 5000, CancellationToken CancellationToken = default)
    {
        ByteReader Reader;

        try
        {
            Reader = await ReceiveHandshakeReaderAsync(TimeoutMs, CancellationToken);
        }
        catch (Exception Ex)
        {
            if (Ex is TimeoutException ToEx ||
                (Ex is not OperationCanceledException && Ex is not TaskCanceledException))
            {
                Logger.LogWarning($"Client connect exception: {Ex}");
            }
            return false;
        }

        string TextReceived = "";
        Reader.Serialize(ref TextReceived);

        if (TextReceived != "Hello")
        {
            Logger.LogWarning("Client sent an invalid packet during connect.");
            return false;
        }

        var Writer = PooledByteWriter.Rent<NetaConnection_HandleHandshake_Server>();

        string OutText = "Understandable";
        Writer.Serialize(OutText);
        SendHandshakeWriter(Writer);
        Writer.Return();
        return true;
    }

    class NetaConnection_HandleHandshake_Client { }
    protected virtual async Task HandleHandshake_Client(int TimeoutMs = 5000, CancellationToken CancellationToken = default)
    {
        var Writer = PooledByteWriter.Rent<NetaConnection_HandleHandshake_Client>();

        string OutText = "Hello";
        Writer.Serialize(OutText);

        SendHandshakeWriter(Writer);
        Writer.Return<NetaConnection_HandleHandshake_Client>();

        var Reader = await ReceiveHandshakeReaderAsync(TimeoutMs, CancellationToken).ConfigureAwait(false);

        string TextReceived = "";
        Reader.Serialize(ref TextReceived);

        if (TextReceived != "Understandable")
        {
            throw new InvalidOperationException("Server sent an invalid packet during connect.");
        }
    }



    /// <summary>
    /// 
    /// </summary>
    /// <exception cref="TimeoutException"></exception>
    /// <exception cref="ChannelClosedException"></exception>
    /// <exception cref="OperationCanceledException"></exception>
    /// <exception cref="InvalidOperationException"></exception>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="SocketException"></exception>
    /// <exception cref="ObjectDisposedException"></exception>
    public async Task ConnectAsync(int TimeoutMs = 5000, CancellationToken Ct = default)
    {

        if (ConnectionHasFlags(NetaConnectionFlags.Server))
        {
            throw new InvalidOperationException("ConnectAsync is not allowed on server.");
        }

        if (ConnectionHasFlags(NetaConnectionFlags.Connected))
        {
            throw new InvalidOperationException("Already connected.");
        }

        Ct.ThrowIfCancellationRequested();

        if (ConnectionHasFlags(NetaConnectionFlags.Error))
        {
            throw new InvalidOperationException("Cannot connect, connection is faulted.");
        }

        ConnectionSetFlags(NetaConnectionFlags.Pending);

        await HandleHandshake_Client(TimeoutMs, Ct).WaitAsync(TimeSpan.FromMilliseconds(TimeoutMs), Ct);

        ConnectionFlipFlags(NetaConnectionFlags.Pending | NetaConnectionFlags.Connected);
    }



    protected void ProcessIncomingAcks(InPacket Packet)
    {
        var Acks = PooledList<FlightAck>.Rent(64);
        Packet.Serialize(Acks);

        var TicksNow = ParallelTickManager.ThisTickTicks;
        foreach (var Ack in Acks)
        {
            if (!OutPacketWindow.Acknowledge(Ack.Id, out var NumTries)) continue;

            if (NumTries == 0 && Ack.Ticks != 0)
            {
                PacketStats.UpdateRtt(TicksNow - Ack.Ticks, Ack.ProcessMilliseconds);
            }

        }
        Acks.Return();
    }








    protected void EnqueueOutgoingAck(InPacket Packet, Int64 TicksNow)
    {
#if NETA_DEBUG
        if (ConnectionHasFlags(NetaConnectionFlags.Shutdown))
        {
            Logger.LogWarning("AddOutgoingAck: Cannot add ack while shutdown");
            return;
        }
#endif
        var DueTicks = TicksNow + PacketStats.GetRetransmissionTimeoutTicks() / 2;
        PendingAck AckPkt = new PendingAck(Packet.Id, DueTicks, Packet.Serialize<Int64>(), TicksNow);
        OutgoingAcksQueue.Enqueue(AckPkt);
    }

    internal void Dispatch_IncomingPacket(PooledInPacket Packet)
    {
        PacketStats.IncrementInPacket();

        Packet.Init();

        if ((Packet.Flags & EPacketFlags.Reliable) != 0)
        {
            EnqueueOutgoingAck(Packet, ParallelTickManager.ThisTickTicks);
        }

        bool SeenBefore = PacketWindow.CheckPacket(Packet.Id) != PacketWindowStatus.New;

        if (SeenBefore)
        {
            Packet.Return();
            return;
        }

        if ((Packet.Flags & EPacketFlags.HasAcks) != 0)
        {
            ProcessIncomingAcks(Packet);

            if ((Packet.Flags & EPacketFlags.HasAcksOnly) != 0)
            {
                Packet.Return();
                return;
            }
        }

        if ((Packet.Flags & EPacketFlags.Fragment) != 0)
        {
            var PacketCombined = PktFragmentationHandler.Process(Packet);
            if (PacketCombined == null) { return; }
            OnReliablePacket(PacketCombined);
            return;
        }

        switch (Packet.Message)
        {
            case EProtocolMessage.None:
                throw new InvalidOperationException();
            case EProtocolMessage.Connect:
                ConnectReceiveQueue.Writer.TryWrite(new ByteReader(Packet)); Packet.Return(); return;
            case EProtocolMessage.Ping: HandlePing_Local(Packet); Packet.Return(); return;
            case EProtocolMessage.Pong: HandlePong_Remote(Packet); Packet.Return(); return;
            case EProtocolMessage.Reliable:
                OnReliablePacket(Packet);
                return;
            case EProtocolMessage.Unreliable:
                OnUnreliablePacket(Packet);
                return;
#if NETA_DEBUG
            default:
                Logger.Log(ELogLevel.Warning, $"Invalid packet received.\n\tId: {Packet.Id} Message: {Packet.Message}");
                Packet.Return();
                return;
#endif
        }
    }

    unsafe void ReceivePacket(PooledInPacket Packet)
    {
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

    void Shutdown_TransportLayer()
    {
        
    }

    protected virtual void Cleanup_Transport()
    {
        if (!ConnectionHasFlags(NetaConnectionFlags.Server))
        {
            Socket.Dispose();
        }

        ConnectReceiveQueue.Writer.TryComplete();

        //while (SendReliableQueue.TryDequeue(out var RelPacket)) RelPacket.Return();

        OutPacketWindow.Sweep(Int64.MaxValue, PacketStats.GetRetransmissionTimeoutTicks(), (Pkt) => Pkt.Return());

        var Packets = PooledList<PooledOutPacket>.Rent();
        var RelPackets = PooledList<PooledOutPacket>.Rent();
        foreach (var Packet in Packets) Packet.Return();
        foreach (var Packet in RelPackets) Packet.Return();
        Packets.Return();
        RelPackets.Return();

        PktFragmentationHandler.Reset();
    }
}