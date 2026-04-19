using Astral.Network.Connections;
using Astral.Network.Drivers;
using Astral.Network.Enums;
using Astral.Network.Sockets;
using Astral.Network.Toolkit;
using Astral.Network.Tools;
using Astral.Network.Transport;
using Astral.Threading;
using Astral.Tick;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Astral.Network.Servers;

public partial class NetaServer
{
    public PacketStatistics PacketStats { get; private set; }

    //EndPoint ReceiveEndPoint = new IPEndPoint(IPAddress.Any, 0);
    // AtomicQueue<(Socket Socket, PooledInPacket Packet, EndPointKey EndPointKey)> IncomingQueue = new AtomicQueue<(Socket Socket, PooledInPacket Packet, EndPointKey EndPointKey)>(Mode: AtomicQueue<(Socket Socket, PooledInPacket Packet, EndPointKey EndPointKey)>.Mode.MPSC);

    

    public event Action<NetaConnection>? OnConnectionOpened;


    private readonly List<Task>[] WorkerConnectTasks = new List<Task>[ParallelTickManager.WorkerCount];
    private readonly object[] WorkerConnectTaskLocks = new object[ParallelTickManager.WorkerCount];


    List<NetaConnection>[] WorkerConnectionRemoveQueue = new List<NetaConnection>[ParallelTickManager.WorkerCount];
    


    


    void Initialize_Transport(IPEndPoint LocalEndPoint, int RecBufferSize = 2 * 1024 * 1024, int SendBufferSize = 2 * 1024 * 1024)
    {
        Initialize_Receiver(LocalEndPoint, RecBufferSize, SendBufferSize);
#if LINUX
        Initialize_TransmitterLinux(); 
#endif
    }

    void Start_Transport()
    {
#if LINUX
        Start_TransportLinuxReceiver();
        Start_TransportLinuxTransmitter();
#else
        PostReceives();
        //ReceiveParallelTickHandle = ParallelTickManager.RegisterParallelTick(ParallelTick_SocketReceive, 480);
#endif
    }



    void ProcessPacket(PooledInPacket Packet, SocketAsyncEventArgs e)
    {
        if (e.BytesTransferred < 1)
        {
            Packet.Return();
            return;
        }

        var EndPoint = e.RemoteEndPoint!;
        //Dispatch_IncomingPacket(Packet, EndPoint);
    }

    public void ParallelTick_Connections(int WorkerIndex)
    {
        var Conns = ClientConnections.GetLocalList();

        foreach (var Conn in Conns)
        {
            try
            {
                Conn.Tick_Remote(WorkerIndex);
            }
            catch (Exception Ex)
            {
                Logger.LogError($"Connection exception: {Ex.ToString()}");
                ClientConnections.EnqueueRemove(Conn, true);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ParallelTick_Transport(int WorkerIndex)
    {
//#if LINUX
//        ParallelTick_SocketReceive(WorkerIndex);
//#endif
        ParallelTick_Receive(WorkerIndex);

        ParallelTick_Connections(WorkerIndex);
#if LINUX
        ParallelTick_Send(WorkerIndex);
#endif

        ClientConnections.Tick_RemoveQueue(WorkerIndex);
    }

    // object NewConnLock = new object();
    NetaConnection HandleNewConnection(NetaSocket Socket, InPacket Packet, ref NetaAddress NetaRemoteAddress, int WorkerIndex)
    {
        if (ClientConnections.EndPointConnectionMap.TryGetValue(NetaRemoteAddress, out var ExistingConn)) return ExistingConn;

        NetaConnection NewConn = CreateConnection();
        NewConn.Server = this;
        NewConn.WorkerIndex = WorkerIndex;
        NewConn.ConnectionSetFlags(NetaConnectionFlags.Pending | NetaConnectionFlags.Handshaking);
        NewConn.InitRemoteConnection(Driver, Socket, NetaRemoteAddress, new PacketStatistics());

        ClientConnections.Add(NewConn, ref NetaRemoteAddress);

        Task HandshakeTask = Task.Run(async () =>
        {
            bool Success = await NewConn.HandleHandshake_Server(NetaDriver.ConnectTimeout, Cts.Token).ConfigureAwait(false);
            NewConnectionHandshakeResult(NewConn, Success);
        });

        return NewConn;
    }

    void NewConnectionHandshakeResult(NetaConnection Connection, bool Success)
    {
        if (!Success)
        {
            ClientConnections.EnqueueRemove(Connection, true);
            Connection.Shutdown();
            Logger.LogWarning($"Client[{Connection.NetaRemoteAddr.GetAddressString()}] connecting failed.");
            return;
        }

        Connection.ConnectionFlipFlags(NetaConnectionFlags.Pending | NetaConnectionFlags.Handshaking | NetaConnectionFlags.Connected);

        OnConnectionOpened?.Invoke(Connection);
    }

    unsafe void ReceivePacket(NetaSocket Socket, PooledInPacket Packet, NetaAddress RemoteEndPointKey, int WorkerIndex)
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

        NetaConnection? NewConn = null;

        try
        {
            if (!ClientConnections.TryGetConnection(ref RemoteEndPointKey, out NewConn))
            {
                if (!ClientConnections.IsEndPointConnectionAllowed(RemoteEndPointKey))
                {
                    Packet.Return();
                    return;
                }

                NewConn = HandleNewConnection(Socket, Packet, ref RemoteEndPointKey, WorkerIndex);
            }

            if (NewConn.ConnectionHasFlags(NetaConnectionFlags.Shutdown))
            {
                Packet.Return();
                return;
            }
            NewConn.Dispatch_IncomingPacket(Packet);
        }
        catch (Exception Ex)
        {
            Logger.LogError($"Connection receive exception: {Ex.ToString()}");

            ClientConnections.Remove(ref RemoteEndPointKey, true);
            throw;
        }
    }

    internal void ConnectionClosed(NetaConnection Conn)
    {
        ClientConnections.EnqueueRemove(Conn);
    }




    void Shutdown_Transport()
    {
    }

    void Cleanup_Transport(int WorkerIndex)
    {
        Cleanup_TransportReceiver(WorkerIndex);
#if !LINUX
        Socket.Close();
#endif
    }
}