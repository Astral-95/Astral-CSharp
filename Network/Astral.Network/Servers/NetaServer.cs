using Astral.Logging;
using Astral.Network.Connections;
using Astral.Network.Drivers;
using Astral.Network.Sockets;
using Astral.Network.Tools;
using Astral.Tick;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Astral.Network.Servers;

public partial class NetaServer
{
    AstralLogger Logger;
    public NetaDriver Driver { get; private set; }
    public CancellationTokenSource Cts { get; private set; }

    //NetaSocket Socket { get; set; }

#if !LINUX
    internal NetaSocket Socket { get; set; }
#else
    internal List<NetaSocket> Sockets { get; set; } = new List<NetaSocket>(); 
#endif

    TickHandle[] TickHandles;


    public event Action<NetaConnection>? OnConnectionClosed;

    public ClientConnections ClientConnections { get; private set; } = new ClientConnections(ParallelTickManager.WorkerCount, 1024);
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
#if LINUX
        for (int i = 0; i < WorkerOutgoingQueue.Length; i++) WorkerOutgoingQueue[i] = []; 
#endif
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
    public void Init(NetaDriver Driver, IPEndPoint LocalEndPoint, PacketStatistics? PktStats = null, int RecBufferSize = 128 * 1024 * 1024, int SendBufferSize = 128 * 1024 * 1024)
    {
        Cts = new CancellationTokenSource();
        Logger = CreateLogger();
        this.Driver = Driver;
        Driver.Server = this;

        if (PktStats == null) PktStats = new PacketStatistics();
        PacketStats = PktStats;

        Initialize_Transport(LocalEndPoint, RecBufferSize, SendBufferSize);

        TickHandles = new TickHandle[ParallelTickManager.WorkerCount];
    }



    public void Start()
    {
        Start_Transport();

        for (int i = 0; i < ParallelTickManager.WorkerCount; i++)
        {
            TickHandles[i] = ParallelTickManager.Register(ParallelTick, WorkerIndex: i);
        }
    }


    public void ParallelTick()
    {
        int WorkerIndex = ParallelTickManager.WorkerIndex;

        if (ShutdownRequested)
        {
            Cleanup(WorkerIndex);
            return;
        }

        ParallelTick_Transport(WorkerIndex);
    }




    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetWorkerIndexForAddress(SocketAddress Address)
    {
        // SocketAddress layout: [0-1] Family, [2-3] Port, [4-7] IP...
        // We hash the port and last part of IP for a quick spread
        int Hash = 0;
        unsafe
        {
            // Access the raw buffer inside SocketAddress via pointer if possible,
            // or just use the indexer. Indexer is safer but slightly slower.
            Hash = Address[2] | (Address[3] << 8) | (Address[4] << 16) | (Address[5] << 24);
        }

        // Ensure positive result and modulo
        return (Hash & 0x7FFFFFFF) % ParallelTickManager.WorkerCount;
    }


    public void Shutdown()
    {
        if (ShutdownRequested) return;
        Cts.Cancel();
        ShutdownRequested = true;      

        Shutdown_Transport();
        ClientConnections.Shutdown();
    }

    protected void Cleanup(int WorkerIndex)
    {
        ref var Handle = ref TickHandles[WorkerIndex];

        if (!Handle.IsValid())
        {
            NetGuard.Fail("Test");
        }

        ParallelTickManager.Unregister(ref Handle);
        Cleanup_Transport(WorkerIndex);
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

        await ClientConnections.WaitForCompletionAsync();
    }
}