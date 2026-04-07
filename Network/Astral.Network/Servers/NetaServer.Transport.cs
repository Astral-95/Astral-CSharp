using Astral.Network.Connections;
using Astral.Network.Drivers;
using Astral.Network.Enums;
using Astral.Network.Sockets;
using Astral.Network.Toolkit;
using Astral.Network.Tools;
using Astral.Network.Transport;
using Astral.Tick;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Astral.Network.Servers;

public partial class NetaServer
{
    [System.Runtime.CompilerServices.InlineArray(NetaConsts.BufferMaxSizeBytes)]
    public struct PacketBuffer
    {
        private byte _;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IOVector
    {
        public IntPtr Base;   // pointer to buffer
        public IntPtr Length; // length of buffer
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MsgHdr
    {
        public IntPtr msg_name;       // pointer to sockaddr
        public int msg_namelen;    // size of sockaddr
        public IntPtr msg_iov;        // pointer to IOVector array
        public IntPtr msg_iovlen;     // number of IOVectors (1 per packet)
        public IntPtr msg_control;    // 0
        public IntPtr msg_controllen; // 0
        public int msg_flags;      // 0
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Mmsghdr
    {
        public MsgHdr msg_hdr;
        public uint msg_len; // filled in by kernel on return
    }

    public PacketStatistics PacketStats { get; private set; }

    //EndPoint ReceiveEndPoint = new IPEndPoint(IPAddress.Any, 0);
    // AtomicQueue<(Socket Socket, PooledInPacket Packet, EndPointKey EndPointKey)> IncomingQueue = new AtomicQueue<(Socket Socket, PooledInPacket Packet, EndPointKey EndPointKey)>(Mode: AtomicQueue<(Socket Socket, PooledInPacket Packet, EndPointKey EndPointKey)>.Mode.MPSC);

    

    public event Action<NetaConnection>? OnConnectionOpened;

    public List<NetaConnection>[] WorkerClientConnections = new List<NetaConnection>[ParallelTickManager.WorkerCount];
    public object ClientConnectionsListLock = new();
    public ConcurrentDictionary<EndPointKey, NetaConnection> EndPointConnectionMap { get; internal set; } = new();

    private readonly object RecentlyClosedEndPointsQueueLock = new object();
    Queue<(EndPointKey Endpoint, DateTime Expiry)> RecentlyClosedEndPointsQueue = new();
    private readonly Dictionary<EndPointKey, byte> RecentlyClosedEndPointsMap = new();

    public ConcurrentDictionary<EndPointKey, bool> BlockedEndPoints { get; internal set; } = new();

    private readonly List<Task>[] WorkerConnectTasks = new List<Task>[ParallelTickManager.WorkerCount];
    private readonly object[] WorkerConnectTaskLocks = new object[ParallelTickManager.WorkerCount];


    List<NetaConnection>[] WorkerConnectionRemoveQueue = new List<NetaConnection>[ParallelTickManager.WorkerCount];
    


    int ReceiveArgsPerSocket = 16;
    List<SocketAsyncEventArgs> SocketArgs = new List<SocketAsyncEventArgs>(64);


    void Initialize_Transport(IPEndPoint LocalEndPoint, int RecBufferSize = 2 * 1024 * 1024, int SendBufferSize = 2 * 1024 * 1024)
    {
        Initialize_Receiver(LocalEndPoint, RecBufferSize, SendBufferSize);
        Initialize_Transmitter();
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


    void ParallelTick_ProcessConnectionsRemoveQueue(int WorkerIndex)
    {
        var Queue = WorkerConnectionRemoveQueue[WorkerIndex];

        foreach (var Connection in Queue)
        {
            lock (ClientConnectionsListLock) WorkerClientConnections[WorkerIndex].Remove(Connection);
        }
    }

    public void ParallelTick_Connections(int WorkerIndex)
    {
        var Conns = WorkerClientConnections[WorkerIndex];

        foreach (var Conn in Conns)
        {
            Conn.Tick_Remote(WorkerIndex);
        }
    }

    public void ParallelTick()
    {
        if (ShutdownRequested) return;
        int WorkerIndex = ParallelTickManager.WorkerIndex;

        ParallelTick_ProcessConnectionsRemoveQueue(WorkerIndex);
        ParallelTick_Receive(WorkerIndex);

        //while (IncomingQueues[WorkerIndex].TryDequeue(out var Item))
        //{
        //    try
        //    {
        //        ReceivePacket(Item.Socket, Item.Packet, Item.EndPointKey, WorkerIndex);
        //    }
        //    catch (Exception Ex)
        //    {
        //        if (Cts.IsCancellationRequested || Ex is ObjectDisposedException) break;
        //        if (Ex is SocketException SocketEx && SocketEx.SocketErrorCode == SocketError.ConnectionReset)
        //        {
        //            if (SocketEx.SocketErrorCode == SocketError.OperationAborted) break;
        //            if (SocketEx.SocketErrorCode == SocketError.ConnectionReset)
        //            {
        //                Logger.Log(ELogLevel.Error, $"{Ex}");
        //                continue;
        //            }
        //        }
        //
        //        Logger.Log(ELogLevel.Critical, $"{Ex}"); break;
        //    }
        //}

        ParallelTick_Connections(WorkerIndex);
#if LINUX
        ParallelTick_Send(WorkerIndex);
#endif
    }

    // object NewConnLock = new object();
    NetaConnection HandleNewConnection(NetaSocket Socket, InPacket Packet, EndPointKey RemoteEndPointKey, int WorkerIndex)
    {
        if (EndPointConnectionMap.TryGetValue(RemoteEndPointKey, out var ExistingConn)) return ExistingConn;

        NetaConnection NewConn = CreateConnection();
        NewConn.Server = this;
        NewConn.WorkerIndex = WorkerIndex;
        NewConn.ConnectionSetFlags(NetaConnectionFlags.Pending | NetaConnectionFlags.Handshaking);
        NewConn.InitRemoteConnection(Driver, Socket, RemoteEndPointKey, Mode, new PacketStatistics());

        lock (ClientConnectionsListLock) WorkerClientConnections[WorkerIndex].Add(NewConn);
        EndPointConnectionMap.TryAdd(RemoteEndPointKey, NewConn);

        Task HandshakeTask = Task.Run(async () =>
        {
            bool Success = await NewConn.HandleHandshake_Server(NetaDriver.ConnectTimeout, Cts.Token).ConfigureAwait(false);
#if !NETA_DEBUG
				//bool Success = await NewSocket.HandleConnect_Server(Cts.Token).ConfigureAwait(false);
#else
            //bool Success = await AsyncUtils.ExecuteTimeoutCrash(() => NewSocket.HandleConnect_Server(Cts.Token), 1000, "HandleNewConnection Timeout").ConfigureAwait(false);
#endif
            NewConnectionHandshakeResult(NewConn, Success);
        });

        //WorkerConnectTasks[WorkerIndex].Add(HandshakeTask);
        //
        //HandshakeTask.ContinueWith(t =>
        //{
        //    lock (WorkerConnectTaskLocks[WorkerIndex]) { WorkerConnectTasks[WorkerIndex].Remove(t); }
        //});
        return NewConn;
    }

    void NewConnectionHandshakeResult(NetaConnection Connection, bool Success)
    {
        if (!Success)
        {
            EndPointConnectionMap.TryRemove(Connection.RemoteEndPointKey, out var _);
            BlockedEndPoints.TryAdd(Connection.RemoteEndPointKey, true);
            Connection.Shutdown();
            Logger.LogWarning($"Client[{Connection.RemoteEndPoint.ToString()}] connecting failed.");
            return;
        }

        Interlocked.Increment(ref NumClientsConnected);

        Connection.ConnectionFlipFlags(NetaConnectionFlags.Pending| NetaConnectionFlags.Handshaking| NetaConnectionFlags.Connected);

        OnConnectionOpened?.Invoke(Connection);
    }

    void ReceivePacket(NetaSocket Socket, PooledInPacket Packet, EndPointKey RemoteEndPointKey, int WorkerIndex)
    {
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

        NetaConnection? NewConn = null;

        EndPointConnectionMap.TryGetValue(RemoteEndPointKey, out NewConn);

        if (NewConn == null)
        {
            if (BlockedEndPoints.ContainsKey(RemoteEndPointKey) || RecentlyClosedEndPointsMap.ContainsKey(RemoteEndPointKey))
            {
                Packet.Return();
                return;
            }

            NewConn = HandleNewConnection(Socket, Packet, RemoteEndPointKey, WorkerIndex);
        }

        NewConn.Dispatch_IncomingPacket(Packet);
    }




    void TryRemoveEndPoint(ref EndPointKey EndPointKey, int WorkerIndex)
    {
        if (EndPointConnectionMap.TryRemove(EndPointKey, out var Conn))
        {
            Interlocked.Decrement(ref NumClientsConnected);
            lock (ClientConnectionsListLock) WorkerClientConnections[WorkerIndex].Remove(Conn);
            Conn.Shutdown();
        }

        OnEndPointRemoved(EndPointKey);
    }

    void TryRemoveEndPoint(EndPointKey EndPointKey, int WorkerIndex, Exception? Ex = null)
    {
        if (!EndPointConnectionMap.TryRemove(EndPointKey, out var Conn))
        {
            BlockedEndPoints.TryAdd(EndPointKey, true);
            if (Ex != null) Logger.LogCritical($"TryRemoveConnection: EndPoint is not registered and threw: {Ex}");
        }
        else
        {
            lock (ClientConnectionsListLock) WorkerClientConnections[WorkerIndex].Remove(Conn);
            BlockedEndPoints.TryAdd(EndPointKey, true);
            Conn.Shutdown();
        }

        OnEndPointRemoved(EndPointKey);
    }


    void OnEndPointRemoved(EndPointKey EndPointKey)
    {
        lock (RecentlyClosedEndPointsQueueLock)
        {
            RecentlyClosedEndPointsMap.TryAdd(EndPointKey, 0);
            RecentlyClosedEndPointsQueue.Enqueue((EndPointKey, DateTime.UtcNow.AddSeconds(2)));

            if (RecentlyClosedEndPointsMap.Count == 1)
            {
                EndpointExpirationCleanupTickId = ParallelTickManager.Register(EndpointExpirationCleanupTick);
            }
        }
    }

    long EndpointExpirationCleanupTickId = 0;
    void EndpointExpirationCleanupTick()
    {
        lock (RecentlyClosedEndPointsQueueLock)
        {
            var Now = DateTime.UtcNow;

            while (RecentlyClosedEndPointsQueue.Count > 0 && RecentlyClosedEndPointsQueue.Peek().Expiry <= Now)
            {
                var Expired = RecentlyClosedEndPointsQueue.Dequeue();
                RecentlyClosedEndPointsMap.Remove(Expired.Endpoint);
            }

            if (RecentlyClosedEndPointsQueue.Count == 0)
            {
                ParallelTickManager.Unregister(EndpointExpirationCleanupTickId);
            }
        }
    }


    internal void ConnectionClosed(NetaConnection Conn)
    {
        Interlocked.Decrement(ref NumClientsConnected);
        OnEndPointRemoved(Conn.RemoteEndPointKey);
        WorkerConnectionRemoveQueue[Conn.WorkerIndex].Add(Conn);

        if (!EndPointConnectionMap.Remove(Conn.RemoteEndPointKey, out var _))
        {
            Logger.LogError($"ConnectionClosed: EndPoint is not registered.");
        }
        else
        {
            OnConnectionClosed?.Invoke(Conn);

            lock (ClientConnectionsListLock) WorkerClientConnections[Conn.WorkerIndex].Remove(Conn);;
        }
    }



    const int SHUT_RD = 0;
    const int SHUT_WR = 1;
    const int SHUT_RDWR = 2;

    [DllImport("libc", SetLastError = true)]
    static extern int shutdown(int sockfd, int how);

    void Shutdown_Transport()
    {
#if LINUX
        AutoParallelTickManager.UnregisterParallelTick(ParallelTickHandle);
#endif
        foreach (var Socket in Sockets)
        {
            //int SocketFd = (int)Socket.SafeHandle.DangerousGetHandle();
            Socket.Close();
        }
    }
}