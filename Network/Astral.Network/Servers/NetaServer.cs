using Astral.Logging;
using Astral.Network.Connections;
using Astral.Network.Drivers;
using Astral.Network.Sockets;
using Astral.Network.Tools;
using Astral.Tick;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Astral.Network.Servers;

public partial class NetaServer
{
    AstralLogger Logger;
    public NetaDriver Driver { get; private set; }
    public CancellationTokenSource Cts { get; private set; }

    //NetaSocket Socket { get; set; }
    internal List<NetaSocket> Sockets { get; set; } = new List<NetaSocket>();

    public NetaConnectionMode Mode { get; private set; }
    ParallelTickHandle ParallelTickHandle;


    public event Action<NetaConnection>? OnConnectionClosed;


    public int NumClientsConnected = 0;

    public ConcurrentDictionary<EndPoint, ClientConnection> EndPointPendingConnectionMap { get; internal set; } = new();
    //public HashSet<EndPoint> BlockedEndPoints { get; internal set; } = new();

    //private List<Task> ConnectTasks { get; set; } = new();
    //private readonly object ConnectTasksLock = new();

    bool ShutdownRequested = false;

    public long TotalBytes { get; set; } = 0;

#pragma warning disable CS8618
    public NetaServer()
#pragma warning restore CS8618
    {
        for (int i = 0; i < WorkerConnectTasks.Length; i++) WorkerConnectTasks[i] = [];
        for (int i = 0; i < WorkerConnectTaskLocks.Length; i++) WorkerConnectTaskLocks[i] = new();
        for (int i = 0; i < WorkerConnectionRemoveQueue.Length; i++) WorkerConnectionRemoveQueue[i] = [];
        for (int i = 0; i < WorkerOutgoingQueue.Length; i++) WorkerOutgoingQueue[i] = [];
        for (int i = 0; i < WorkerClientConnections.Length; i++) WorkerClientConnections[i] = [];


    }
    protected virtual AstralLogger CreateLogger()
    {
        return AstralLoggingCenter.CreateLogger("NetaServer");
    }

    protected virtual ClientConnection CreateConnection()
    {
        return new ClientConnection(this);
    }


    void SetReusePort(NetaSocket S)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // SO_REUSEPORT = 15 on Linux kernel
            S.SetRawSocketOption(1, 15, BitConverter.GetBytes(1));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows doesn't have true SO_REUSEPORT load balancing
            // ReuseAddress is the closest but won't distribute packets
            S.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // SO_REUSEPORT = 512 on macOS
            S.SetRawSocketOption(1, 512, BitConverter.GetBytes(1));
        }
    }
    public void Init(NetaDriver Driver, IPEndPoint LocalEndPoint, NetaConnectionMode Mode, PacketStatistics? PktStats = null, int RecBufferSize = 16 * 1024 * 1024, int SendBufferSize = 16 * 1024 * 1024)
    {
        Cts = new CancellationTokenSource();
        Logger = CreateLogger();
        this.Driver = Driver;
        Driver.Server = this;
        this.Mode = Mode;

        if (PktStats == null) PktStats = new PacketStatistics();
        PacketStats = PktStats;

        Initialize_Transport(LocalEndPoint, RecBufferSize, SendBufferSize);



        //Socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        //{
        //	//Socket.DualMode = true;
        //
        //	ReceiveBufferSize = RecBufferSize,
        //	SendBufferSize = SendBufferSize
        //};		
        //
        //Socket.Bind(LocalEndPoint);
    }



    public void Start()
    {
        switch (Mode)
        {
            case NetaConnectionMode.Auto: StartAuto(); break;
            case NetaConnectionMode.AutoDeferred: StartAutoDeferred(); break;
            case NetaConnectionMode.Manual: break;
            default: throw new InvalidOperationException();
        }
    }


    void StartAuto()
    {
        throw new NotImplementedException();
    }


    void StartAutoDeferred()
    {
        Start_TransportReceiver();
        ParallelTickHandle = ParallelTickManager.RegisterParallelTick(ParallelTick);
    }



    public void Shutdown()
    {
        if (ShutdownRequested) return;
        Cts.Cancel();
        ShutdownRequested = true;
        if (ParallelTickHandle.IsValid()) ParallelTickManager.UnregisterParallelTick(ParallelTickHandle);

        Shutdown_Transport();

        var MapSnapshot = EndPointConnectionMap.ToArray();
        foreach (var Pair in MapSnapshot)
        {
            Pair.Value.Shutdown();
        }
    }

    public async Task WaitForCompletionAsync()
    {
        for (int i = 0; i < ParallelTickManager.WorkerCount; i++)
        {
            while (WorkerConnectTasks[i].Count() > 0)
            {
                int LastIndex = WorkerConnectTasks[i].Count - 1;

                try { await WorkerConnectTasks[i][LastIndex]; } catch { }
            }
        }

        foreach (var Kvp in EndPointConnectionMap)
        {
            await Kvp.Value.WaitForCompletionAsync();
        }

        foreach (var Clients in WorkerClientConnections)
        {
            Clients.Clear();
        }

        EndPointConnectionMap.Clear();
    }
}