using Astral.Network.Channels;
using Astral.Network.Connections;
using Astral.Network.Servers;
using Astral.Network.Transport;
using System.Diagnostics;

namespace Astral.Network.Tests.Tools;

public class NetCodeTestM2SendManager
{
    NetCodeTestSettings Settings = new NetCodeTestSettings();

    readonly NetaServer? Server;
    readonly List<ServerConnection> Clients = new();
    DebugPacketSettings ServerDebugPacketSettings = new DebugPacketSettings();
    DebugPacketSettings ClientsDebugPacketSettings = new DebugPacketSettings();
    readonly HashSet<NetCodeTestM2Sender> Senders = new();
    readonly CancellationToken Ct;
    readonly CancellationTokenSource Cts = new();
    long DueTicks;


    public Stopwatch Sw = Stopwatch.StartNew();

    Task? WaitTask { get; set; }

    public NetCodeTestM2SendManager(CancellationToken Ct = default)
    {
        this.Ct = Ct;
    }



    public void Start(NetCodeTestSettings Settings)
    {
        if (Ct.IsCancellationRequested) throw new OperationCanceledException();
        this.Settings = Settings;
        ServerDebugPacketSettings = new DebugPacketSettings
        {
            FixedBodySize = Settings.ServerPackets.FixedBodySize,
            FixedBodySizeBytes = Settings.ServerPackets.FixedBodySizeBytes,
            MaxBodySizeBytes = Settings.ServerPackets.MaxBodySizeBytes,
            MinBodySizeBytes = Settings.ServerPackets.MinBodySizeBytes,
        };

        ClientsDebugPacketSettings = new DebugPacketSettings
        {
            FixedBodySize = Settings.ClientPackets.FixedBodySize,
            FixedBodySizeBytes = Settings.ClientPackets.FixedBodySizeBytes,
            MaxBodySizeBytes = Settings.ClientPackets.MaxBodySizeBytes,
            MinBodySizeBytes = Settings.ClientPackets.MinBodySizeBytes,
        };

        this.DueTicks = (long)(Settings.DurationSeconds * Context.ClockFrequency);
        DueTicks += Context.Ticks;

        WaitTask = Task.Run(async () =>
        {
            while (!Ct.IsCancellationRequested && Context.Ticks < DueTicks)
            {
                await Task.Delay(100);
            }

            Sw.Stop();
            Cts.Cancel();
            await Task.Delay(250);
        });
    }

    public void AddServerSender(NetaChannel Channel)
    {
        if (Ct.IsCancellationRequested) throw new InvalidOperationException();
        var Sender = new NetCodeTestM2Sender(Channel, Settings.ServerPackets, ServerDebugPacketSettings!, Cts.Token);
        lock (Senders) Senders.Add(Sender);
        Sender.OnComplete += SenderCompleted;
        Sender.Start();
    }

    public void AddClientSender(NetaChannel Channel)
    {
        if (Ct.IsCancellationRequested) throw new InvalidOperationException();
        var Sender = new NetCodeTestM2Sender(Channel, Settings.ClientPackets, ClientsDebugPacketSettings!, Cts.Token);
        lock (Senders) Senders.Add(Sender);
        Sender.OnComplete += SenderCompleted;
        Sender.Start();
    }

    void SenderCompleted(NetCodeTestM2Sender Sender)
    {
        lock (Senders) Senders.Remove(Sender);
    }

    public async Task WaitForCompletionAsync()
    {
        await WaitTask!;
        await Task.Delay(1000);
    }
}