using Astral.Logging;
using Astral.Network.Channels;
using Astral.Network.Connections;
using Astral.Network.Drivers;
using Astral.Network.Enums;
using Astral.Network.Servers;
using Astral.Network.Transport;
using Astral.Tick;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Astral.Network.Tests.Tools;

internal class NetCodeTestExtensions
{
    public static NetaServer CreateServer(string Address = "127.0.0.1:8000")
    {
        NetaServer Server = new NetaServer();
        Server.Init(new NetaDriver(), AddressConverters.ParseEndPoint(Address)!);
        return Server;
    }
    public static void CreateClients(List<ServerConnection> ClientsList, int NumClients, string Address = "127.0.0.1:8000")
    {
        var Index = ParallelTickManager.WorkerIndex;
        for (int ClientIndex = 0; ClientIndex < NumClients; ClientIndex++)
        {
            var NewClient = new ServerConnection();
            NewClient.InitLocalConnection(new NetaDriver(), AddressConverters.ParseEndPoint(Address)!);

            ClientsList.Add(NewClient);
        }
    }

    public static async Task ConnectClientsAsync(List<ServerConnection> ClientsList, Action<ServerConnection> OnClientConnected, int TimeoutMs = 5000, int MaxConcurrent = 32, CancellationTokenSource? Cts = null)
    {
        if (Cts == null) Cts = new CancellationTokenSource();

        using SemaphoreSlim Semaphore = new SemaphoreSlim(MaxConcurrent);
        List<Task> ConnectTasks = new List<Task>();

        foreach (var Client in ClientsList)
        {
            await Semaphore.WaitAsync(Cts.Token);

            var NewTask = Task.Run(async () =>
            {
                try
                {
                    await ClientConnect(Client, TimeoutMs, Cts.Token).WaitAsync(TimeSpan.FromMilliseconds(TimeoutMs));
                    OnClientConnected?.Invoke(Client);

                }
                catch (Exception Ex)
                {
                    AstralLoggingCenter.Log("ConnectClientsAsync", ELogLevel.Error, Ex.ToString());
                }
                finally
                {
                    Semaphore.Release();
                }

            }, Cts.Token);

            ConnectTasks.Add(NewTask);
        }

        while (ConnectTasks.Count > 0)
        {
            Task Finished = await Task.WhenAny(ConnectTasks);
            ConnectTasks.Remove(Finished);

            if (Finished.IsFaulted)
            {
                Cts.Cancel();
                await Finished;
                return;
            }
        }
    }



    public static async Task WaitClientsConnectAsync(NetaServer Server, int NumClients, CancellationToken Ct = default)
    {
        while (!Ct.IsCancellationRequested && Server.ClientConnections.NumConnected < NumClients)
        {
            await Task.Delay(100);
        }
    }
    public static async Task ClientConnect(ServerConnection Client, int TimeoutMs, CancellationToken Ct = default)
    {
        await Client.ConnectAsync(TimeoutMs, Ct);



        if (!Client.ConnectionHasFlags(NetaConnectionFlags.Connected))
        {
            throw new Exception("Client connecting failed.");
        }
    }

    public static void ResetCounters(NetaServer? Server, List<ServerConnection>? Clients = null)
    {
        if (Server != null)
        {
            foreach (var Connection in Server.ClientConnections)
            {
                Connection.PacketStats.ResetCounters();
            }
        }

        if (Clients == null)
        {
            return;
        }

        foreach (var Cleint in Clients)
        {
            Cleint.PacketStats.ResetCounters();
        }
    }

    //public static async Task ServerSendLoopTask(NetaServer Server, NetCodeTestPacketSettings PacketSettings, CancellationToken Ct = default)
    //{
    //    List<Task> Tasks = new();
    //    foreach (var Kvp in Server!.EndPointConnectionMap)
    //    {
    //        var ClientChannel = Kvp.Value.Channels[0]!;
    //        var NewClientTask = Task.Run(() => ChannelPacketSenderLoopAsync(ClientChannel, PacketSettings, Ct));
    //        Tasks.Add(NewClientTask);
    //    }
    //
    //    Stopwatch Sw = Stopwatch.StartNew();
    //
    //    await Task.WhenAll(Tasks);
    //}

    //public static async Task<double> ServerSendOnceTask(NetaServer Server, NetCodeTestPacketSettings PacketSettings, CancellationToken Ct = default)
    //{
    //	List<Task> Tasks = new();
    //	foreach (var Kvp in Server!.EndPointConnectionMap)
    //	{
    //		var ClientChannel = Kvp.Value.Channels[0]!;
    //		var NewClientTask = Task.Run(() => ChannelPacketSenderFiniteAsync(ClientChannel, PacketSettings, Ct));
    //		Tasks.Add(NewClientTask);
    //	}
    //
    //	Stopwatch Sw = Stopwatch.StartNew();
    //
    //	var ProbTask = Task.Run(async () =>
    //	{
    //		while (!Ct.IsCancellationRequested)
    //		{
    //			await Task.Delay(10);
    //
    //			bool ServerFinished = true;
    //
    //			foreach (var Kvp in Server!.EndPointConnectionMap)
    //			{
    //				var ClientConn = Kvp.Value;
    //
    //				ServerFinished &= ClientConn.OutgoingAcksQueue.Count < 1;
    //				ServerFinished &= ClientConn.OutBunchQueue.Count < 1;
    //				ServerFinished &= ClientConn.OutReliableResendList.Count < 1;
    //				ServerFinished &= Kvp.Value.PacketStats.AppOut >= PacketSettings.NumSends;
    //
    //				if (ServerFinished == false) break;
    //			}
    //
    //			if (ServerFinished || Server!.EndPointConnectionMap.IsEmpty)
    //			{
    //				Sw.Stop();
    //				//await Task.Delay(1000);
    //				break;
    //			}
    //		}
    //	});
    //	
    //	await Task.WhenAll(Tasks);
    //	await ProbTask;
    //	return Sw.Elapsed.TotalMilliseconds;
    //}

    public static async Task ClientsSendLoopTaskAsync(List<ServerConnection> Clients, NetCodeTestPacketSettings PacketSettings, CancellationToken Ct = default)
    {
        List<Task> Tasks = new();
        foreach (var Client in Clients)
        {
            var ClientChannel = Client.Channel!;
            var NewClientTask = Task.Run(() => ChannelPacketSenderLoopAsync(ClientChannel, PacketSettings, Ct));
            Tasks.Add(NewClientTask);
        }
        await Task.WhenAll(Tasks);
    }
    //public static async Task<double> ClientsSendOnceTaskAsync(List<ServerConnection> Clients, NetCodeTestPacketSettings PacketSettings, CancellationToken Ct = default)
    //{
    //	List<Task> Tasks = new();
    //	foreach (var Client in Clients)
    //	{
    //		var ClientChannel = Client.Channel!;
    //		var NewClientTask = Task.Run(() => ChannelPacketSenderFiniteAsync(ClientChannel, PacketSettings, Ct));
    //		Tasks.Add(NewClientTask);
    //	};
    //
    //	Stopwatch Sw = Stopwatch.StartNew();
    //
    //	var ProbTask = Task.Run(async () =>
    //	{
    //		while (!Ct.IsCancellationRequested)
    //		{
    //			await Task.Delay(10);
    //
    //			bool ClientsFinished = true;
    //
    //			foreach (var Client in Clients)
    //			{
    //				ClientsFinished &= Client.OutgoingAcksQueue.Count < 1;
    //				ClientsFinished &= Client.OutReliableResendList.Count < 1;
    //				ClientsFinished &= Client.OutBunchQueue.Count < 1;
    //
    //				ClientsFinished &= Client.PacketStats.AppOut >= PacketSettings.NumSends;
    //
    //				//ClientsFinished &= ClientDriver.PacketStats.AppOut >= Settings.ClientPackets.Num;
    //
    //				//ClientsFinished &= ClientConn.PacketStats.AppIn >= Settings.ServerPackets.Num;
    //				//ClientsFinished &= ClientConn.PacketStats.AppOut >= Settings.ClientPackets.Num;
    //
    //				if (!ClientsFinished)
    //				{
    //					break;
    //				}
    //			}
    //
    //			if (ClientsFinished)
    //			{
    //				Sw.Stop();
    //				//await Task.Delay(1000);
    //				break;
    //			}
    //		}
    //	});
    //
    //	await Task.WhenAll(Tasks);
    //	await ProbTask;
    //	return Sw.Elapsed.TotalMilliseconds;
    //}



    public static async Task ChannelPacketSenderLoopAsync(NetaChannel Channel, NetCodeTestPacketSettings PacketSettings, CancellationToken Ct = default)
    {
        while (!Ct.IsCancellationRequested && Channel.Connection!.ConnectionHasFlags(NetaConnectionFlags.Connected))
        {
            await Task.Delay(Random.Shared.Next(PacketSettings.MinPPS, PacketSettings.MaxPPS));

            if (!Channel.Connection!.ConnectionHasFlags(NetaConnectionFlags.Connected)) { break; }

            var DebugPacketSettings = new DebugPacketSettings
            {
                FixedBodySize = PacketSettings.FixedBodySize,
                FixedBodySizeBytes = PacketSettings.FixedBodySizeBytes,
                MaxBodySizeBytes = PacketSettings.MaxBodySizeBytes,
                MinBodySizeBytes = PacketSettings.MinBodySizeBytes,
            };

            if (Random.Shared.NextDouble() < PacketSettings.ReliablePercentage)
            {
                Channel.SendReliableDebugPacket(DebugPacketSettings);
            }
            else
            {
                Channel.SendUnreliableDebugPacket(DebugPacketSettings);
            }
        }
    }
    //public static async Task ChannelPacketSenderFiniteAsync(NetaChannel Channel, NetCodeTestPacketSettings PacketSettings, CancellationToken Ct = default)
    //{
    //	var NumPackets = PacketSettings.NumSends;
    //
    //	while (!Ct.IsCancellationRequested && NumPackets > 0 &&
    //		(Channel.Connection!.ConnectionFlags & NetaConnectionFlags.Connected) != 0)
    //	{
    //		await Task.Delay(Random.Shared.Next(PacketSettings.MinPPS, PacketSettings.MaxPPS));
    //
    //		if((Channel.Connection!.ConnectionFlags & NetaConnectionFlags.Connected) == 0){ break; }
    //
    //		var DebugPacketSettings = new DebugPacketSettings
    //		{
    //			FixedBodySize = PacketSettings.FixedBodySize,
    //			FixedBodySizeBytes = PacketSettings.FixedBodySizeBytes,
    //			MaxBodySizeBytes = PacketSettings.MaxBodySizeBytes,
    //			MinBodySizeBytes = PacketSettings.MinBodySizeBytes,
    //		};
    //
    //		if (Random.Shared.NextDouble() < PacketSettings.ReliablePercentage)
    //		{
    //			Channel.SendReliableDebugPacket(DebugPacketSettings);
    //		}
    //		else
    //		{
    //			Channel.SendUnreliableDebugPacket(DebugPacketSettings);
    //		}
    //		NumPackets--;
    //	}
    //}




    //public static async Task ServerTickerAsync(NetServer Server, CancellationToken Ct)
    //{
    //	//long LastTicks = Context.Ticks;
    //	//
    //	//var TickRateTicks = Context.ClockFrequency / NetaDriver.TickRate;
    //	//
    //	//while (!Ct.IsCancellationRequested)
    //	//{
    //	//	long NowTicks = Context.Ticks;
    //	//	long DeltaTicks = NowTicks - LastTicks;
    //	//	LastTicks = NowTicks;
    //	//
    //	//	try
    //	//	{
    //	//		await Server!.TickAsync(NowTicks, DeltaTicks);
    //	//	}
    //	//	catch (Exception Ex)
    //	//	{
    //	//		if (Ct.IsCancellationRequested) break;
    //	//		if (Ex is SocketException SocketEx && SocketEx.SocketErrorCode == SocketError.ConnectionReset)
    //	//		{
    //	//			if (SocketEx.SocketErrorCode == SocketError.OperationAborted) break;
    //	//			if (SocketEx.SocketErrorCode == SocketError.ConnectionReset)
    //	//			{
    //	//				AstralLoggingCenter.Log(ELogLevel.Error, $"{Ex}");
    //	//				continue;
    //	//			}
    //	//		}
    //	//
    //	//		AstralLoggingCenter.Log(ELogLevel.Critical, $"{Ex}"); break;
    //	//	}
    //	//
    //	//	long TargetTicks = LastTicks + (long)TickRateTicks;
    //	//	long RemainingTicks = TargetTicks - Context.Ticks;
    //	//
    //	//	if (RemainingTicks > 0)
    //	//	{
    //	//		// convert remaining ticks to milliseconds
    //	//		int DelayMs = (int)(RemainingTicks * 1000 / Context.ClockFrequency);
    //	//		if (DelayMs > 0)
    //	//		{
    //	//			try { await Task.Delay(DelayMs, Ct).ConfigureAwait(false); } catch { }
    //	//		}
    //	//	}
    //	//}
    //}
    //public static async Task ClientsTickerAsync(List<ServerConnection> Clients, AsyncTickSignal TickSignal, CancellationToken Ct)
    //{
    //	long LastTicks = Context.Ticks;
    //
    //	var TickRateTicks = Context.ClockFrequency / NetaDriver.TickRate;
    //
    //	foreach (ServerConnection Client in Clients)
    //	{
    //		Client.Start();
    //	}
    //
    //	while (!Ct.IsCancellationRequested)
    //	{
    //
    //		long NowTicks = Context.Ticks;
    //		long DeltaTicks = NowTicks - LastTicks;
    //		LastTicks = NowTicks;
    //
    //		List<Task> Tasks = new();
    //
    //		try
    //		{
    //			TickSignal.Release();
    //			await TickSignal.WaitCompletionAsync();
    //		}
    //		catch (Exception Ex)
    //		{
    //			if (Ct.IsCancellationRequested) break;
    //			if (Ex is SocketException SocketEx && SocketEx.SocketErrorCode == SocketError.ConnectionReset)
    //			{
    //				if (SocketEx.SocketErrorCode == SocketError.OperationAborted) break;
    //				if (SocketEx.SocketErrorCode == SocketError.ConnectionReset)
    //				{
    //					AstralLoggingCenter.Log(ELogLevel.Error, $"{Ex}");
    //					continue;
    //				}
    //			}
    //
    //			AstralLoggingCenter.Log(ELogLevel.Critical, $"{Ex}"); break;
    //		}
    //
    //		long TargetTicks = LastTicks + (long)TickRateTicks;
    //		long RemainingTicks = TargetTicks - Context.Ticks;
    //
    //		if (RemainingTicks > 0)
    //		{
    //			// convert remaining ticks to milliseconds
    //			int DelayMs = (int)(RemainingTicks * 1000 / Context.ClockFrequency);
    //			if (DelayMs > 0)
    //			{
    //				try { await Task.Delay(DelayMs, Ct).ConfigureAwait(false); } catch { }
    //			}
    //		}
    //	}
    //}


    public static void OnLog(LogEntry Entry)
    {
        ConsoleColor OriginalColor = Console.ForegroundColor;
        string OutputLevelStr = "";

        switch (Entry.Level)
        {
            case ELogLevel.Trace:
                OutputLevelStr = "Trace";
                break;
            case ELogLevel.Debug:
                OutputLevelStr = "Debug";
                break;
            case ELogLevel.Info:
                OutputLevelStr = "Info";
                break;
            case ELogLevel.Warning:
                OutputLevelStr = "Warning";
                break;
            case ELogLevel.Error:
                OutputLevelStr = "Error";
                break;
            case ELogLevel.Critical:
                OutputLevelStr = "Critical";
                break;
            default:
                break;
        }

        Console.WriteLine($"{OutputLevelStr}: {Entry.Message}");
    }


    public static bool OnRunComplete(double ElapsedMs, ConcurrentQueue<string>? Logs = null)
    {
        bool NoErrors = true;
        var Logger = AstralLoggingCenter.CreateLogger("NetCodeTestExtensions");
        var Leaks = PooledObjectsTracker.ReportLeaks();
        if (Leaks != null)
        {
            Logger.LogError(string.Join("\n", Leaks)); NoErrors = false;
        }

        var TotalPooled = PooledObjectsTracker.GetTotalPooledObjects();
        var TotalRented = PooledObjectsTracker.GetRentedObjectsCount();

        Logger.LogInfo($"Total Pooled Objects: {TotalPooled}");
        Logger.LogInfo($"Total Rented Objects: {TotalRented}");
        Logger.LogInfo($"Pkts created: InPacket[{PooledInPacket.NumTnstantiated}] OutPackets[{PooledOutPacket.NumTnstantiated}]");
        Logger.LogInfo($"Bunchs created: InBunches[{InBunch.NumTnstantiated}] OutBunchs[{OutBunch.NumTnstantiated}]");

        if (Logs != null)
        {
            int Num = 0;

            while (Logs.TryDequeue(out var Log))
            {
                Logger.LogDebug(Log);
                Num++;
            }
        }

        return NoErrors;
    }
}