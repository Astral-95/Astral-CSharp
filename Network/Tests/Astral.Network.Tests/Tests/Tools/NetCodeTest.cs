using System.Collections.Concurrent;
using System.Diagnostics;
using Astral.Logging;
using Astral.Network.Connections;
using Astral.Network.Servers;
using Astral.Network.Transport;
using Astral.Tests.TesterTools;
using Astral.Tick;

namespace Astral.Network.Tests.Tools;

//public class NetCodeTest
//{
//	public static NetCodeTestEnd2End? End2End { get; set; }
//	public static NetCodeTestServer? Server { get; set; }
//	public static NetCodeTestClients? Clients { get; set; }
//	static NetCodeTest()
//	{
//		var _ = typeof(AstralLoggingCenter);
//		_ = typeof(AutoParallelTickManager);
//	}
//}

public class NetCodeTest
{
    private static readonly NetCodeTest PrivateInstance = new NetCodeTest();
    public static NetCodeTest Instance => PrivateInstance;
#pragma warning disable CS8618
    static NetCodeTestSettings Settings = null!;
#pragma warning restore CS8618

    public Action? OnStopping;
    public Action? OnFinished;

    public int NumRun { get; private set; } = 0;
    public NetaServer? Server { get; private set; }
    public List<ServerConnection> Clients { get; private set; } = new();

    static ConcurrentQueue<string> Logs = new();

    CancellationTokenSource? Cts = new CancellationTokenSource();
    CancellationTokenSource? RunCts = new CancellationTokenSource();

    public Task? RunTask { get; private set; }
    Task? CollectorTask { get; set; }

    List<Task> Tasks = new List<Task>();

    public Stopwatch Sw { get; private set; } = new Stopwatch();
    public Stopwatch SendSw { get; private set; } = new Stopwatch();

    AstralLogger Logger = AstralLoggingCenter.CreateLogger("NetCodeTest");

    public NetCodeTestM2SendManager? SenderManager;


    static NetCodeTest()
    {
        var _ = typeof(AstralLoggingCenter);
        _ = typeof(ParallelTickManager);

        ParallelTickManager.OnWorkerInit += (WorkerIndex) =>
        {
            //NetaConnection.OutBunchPool = new OutBunchPool(1024);
            OutBunch.PrePopulate(175000);
            PooledInPacket.PrePopulate(5000);
        };

    }
    private NetCodeTest()
    {
        ParallelTickManager.OnError = (Msg) =>
        {
            Logger.LogError($"Tick Manager error: {Msg}");
        };

        ParallelTickManager.Initialize();
    }

    internal void Start(NetCodeTestSettings InSettings)
    {
        if (RunTask != null) throw new InvalidOperationException("Test already running.");
        Cts = new CancellationTokenSource();
        Settings = InSettings;

        RunTask = Task.Run(RunAsync);
    }

    internal async Task StopAsync()
    {
        if (RunTask == null) return;

        if (Cts != null) await Cts.CancelAsync();
        if (RunCts != null) await RunCts.CancelAsync();
        await RunTask;
        Reset();
    }

    internal async Task RunAsync()
    {
        PooledObjectsTracker.ClearForTests();

        _ = Task.Run(async () => {
            while (true)
            {

                await Task.Delay(5000);
                //Context.TickMultiplier = 0.01;
                await Task.Delay(5000);
                // Context.TickMultiplier = 1.0;
            }
        });

        Sw = Stopwatch.StartNew();

        TaskScheduler.UnobservedTaskException += async (sender, args) =>
        {
            args.SetObserved();
            if (args.Exception.InnerExceptions.Count < 1)
            {
                Logger.LogCritical("UnobservedTaskException: " + args.Exception.ToString());
                RunCts?.Cancel();
            }
            else
            {
                foreach (var InnerEx in args.Exception.InnerExceptions)
                {
                    if (InnerEx is TaskCanceledException || InnerEx is OperationCanceledException) return;

                    Logger.LogCritical("UnobservedTaskException: " + InnerEx.ToString());
                }
                RunCts?.Cancel();
            }
            await Task.Delay(1000);
            await StopAsync();
        };

        AppDomain.CurrentDomain.UnhandledException += async (Sender, Args) =>
        {
            var Exception = (Exception)Args.ExceptionObject;

            if (Exception is TaskCanceledException || Exception is OperationCanceledException) return;

            Logger.LogCritical("UnhandledException: " + Exception.ToString());
            RunCts?.Cancel();
            await Task.Delay(1000);
            await StopAsync();
        };

        int Runs = Settings.NumRuns;
        if (Runs < 1) Runs = int.MaxValue;

        try
        {
            for (NumRun = 0; NumRun < Runs && !Cts!.IsCancellationRequested; NumRun++)
            {

                Logger.LogInfo($"Starting run {NumRun + 1}");
                Reset();

                await Task.Delay(500, RunCts!.Token);

                SenderManager = new NetCodeTestM2SendManager(RunCts.Token);
                SenderManager.Start(Settings);

                SendSw = Stopwatch.StartNew();

                var ServEP = AddressConverters.ParseEndPoint(Settings.ServerAddress);

                if (Settings.ServerEnabled)
                {
                    //WinDivertInterceptor.Start($"(udp) and (udp.DstPort == {ServEP!.Port} or udp.SrcPort == {ServEP.Port})");
                    Logger.LogInfo("Creating server.");
                    Server = NetCodeTestExtensions.CreateServer(Settings.ServerAddress);
                    Logger.LogInfo($"Starting server.");
                    Server.Start();
                    Logger.LogInfo($"Started.");

                    Server!.OnConnectionOpened += (NetaConnection Conn) =>
                    {
                        SenderManager.AddServerSender(Conn.Channel);
                    };
                }

                if (Settings.ClientsEnabled)
                {
                    //WinDivertInterceptor.Start($"(udp) and (udp.DstPort == {ServEP!.Port} or udp.SrcPort == {ServEP.Port})");
                    Logger.LogInfo($"Creating clients. Count: {Settings.NumClients}");
                    NetCodeTestExtensions.CreateClients(Clients, Settings.NumClients, Settings.ClientsServerAddress);
                    Logger.LogInfo($"{Settings.NumClients} Clients created.");

                    Logger.LogInfo($"Connecting clients to server.");

                    await NetCodeTestExtensions.ConnectClientsAsync(Clients, (Client) =>
                    {
                        SenderManager.AddClientSender(Client.Channel!);

                    }, TimeoutMs: 50000, MaxConcurrent: 32, Cts: RunCts);

                    Logger.LogInfo($"Clients connected.");
                }



                if (RunCts.IsCancellationRequested) break;
                await SenderManager.WaitForCompletionAsync();
                RunCts.Cancel();

                SendSw.Stop();
                //await Task.Delay(1000, RunCts.Token);

                if (!(Settings.ServerEnabled && Settings.ClientsEnabled) || NumRun + 1 >= Runs)
                {
                    OnStopping?.Invoke();
                    Sw.Stop();
                }

                var ElapsedMSecs = SenderManager.Sw.Elapsed.TotalMilliseconds;

                Logger.LogInfo($"Run {NumRun + 1} Complete. Took {ElapsedMSecs} ms");


                await Task.Delay(250);
                Logger.LogInfo($"Disposing run {NumRun + 1}.");
                Logger.LogInfo($"Disposing packet interceptor...");
                await WinDivertInterceptor.Stop();
                Logger.LogInfo($"Disposing packet interceptor complete.");
                await DisposeAsync();
                await Task.Delay(100);
                if (!NetCodeTestExtensions.OnRunComplete(ElapsedMSecs, Logs))
                {
                    Logger.LogInfo($"Disposing run {NumRun + 1} complete.");
                    break;
                }
                Logger.LogInfo($"Disposing run {NumRun + 1} complete.");

                if (!(Settings.ServerEnabled && Settings.ClientsEnabled)) break;
                await Task.Delay(1500);
            }
        }
        catch (Exception Ex)
        {
            await WinDivertInterceptor.Stop();
            OnStopping?.Invoke();
            Logger.LogError($"Run {NumRun} exception: {Ex}");
            await Task.Delay(1000);
            if (Ex is not OperationCanceledException) Logs.Enqueue(Ex.ToString());
            await Task.Delay(500);
            await DisposeAsync();
            NetCodeTestExtensions.OnRunComplete(0, Logs);
            await Task.Delay(1000);
            Completed(); return;
        }
        //await DisposeAsync();
        Completed();
    }

    void Completed()
    {
        RunTask = null;
        RunCts?.Cancel();
        OnFinished?.Invoke();
    }

    void Reset()
    {
        Logs.Clear();
        Clients.Clear();
        DisposeCalled = false;

        RunCts = new CancellationTokenSource();
    }

    bool DisposeCalled = false;
    async Task DisposeAsync()
    {
        if (DisposeCalled) return;
        DisposeCalled = true;

        RunCts?.Cancel();
        RunCts?.Dispose();
        RunCts = null;
        SenderManager = null;

        if (/*Random.Shared.Next(2) == 0*/ false)
        {
            if (Clients.Count > 0)
            {
                Logger.LogInfo("Disposing clients...");
                await DisposeClientsAsync();
                Logger.LogInfo("Disposing clients complete");
                await Task.Delay(500);
            }

            if (Server != null)
            {
                Logger.LogInfo("Disposing server...");
                await DisposeServerAsync();
                Logger.LogInfo("Disposing server complete");
            }
        }
        else
        {
            if (Server != null)
            {
                Logger.LogInfo("Disposing server...");
                await DisposeServerAsync();
                Logger.LogInfo("Disposing server complete");
                await Task.Delay(500);
            }

            if (Clients.Count > 0)
            {
                Logger.LogInfo("Disposing clients...");
                await DisposeClientsAsync();
                Logger.LogInfo("Disposing clients complete");
            }
        }

        await Task.Delay(500);
        Logger.LogInfo("Collecting garbage...");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Logger.LogInfo("Collecting garbage complete");
    }

    async Task DisposeServerAsync()
    {
        Server!.Shutdown();
        await Server.WaitForCompletionAsync();
        Server = null;
    }

    async Task DisposeClientsAsync()
    {
        foreach (var Client in Clients)
        {
            Client.Shutdown();
        }

        for (int Index = Clients.Count - 1; Index >= 0; Index--)
        {
            await Clients[Index]!.WaitForCompletionAsync();
            //await Task.Delay(25);
            Clients.RemoveAt(Index);
        }

        Clients.Clear();
    }
}