using Astral.Network.Channels;
using Astral.Network.Connections;
using Astral.Network.Enums;
using Astral.Network.Transport;
using Astral.Tick;

namespace Astral.Network.Tests.Tools;

public class NetCodeTestM2Sender
{
    readonly NetaChannel Channel;
    readonly NetaConnection Connection;
    readonly NetCodeTestPacketSettings PacketSettings;
    readonly DebugPacketSettings DebugPktSettings;
    readonly CancellationToken Ct;

    long TickId = 0;
    long LastSendTick;

    public Action<NetCodeTestM2Sender>? OnComplete;

    public NetCodeTestM2Sender(NetaChannel Channel, NetCodeTestPacketSettings PacketSettings, DebugPacketSettings DgPktOpts, CancellationToken Ct = default)
    {
        this.Channel = Channel;
        Connection = Channel.Connection;
        this.PacketSettings = PacketSettings;
        this.DebugPktSettings = DgPktOpts;
        this.Ct = Ct;
    }

    public void Start()
    {
        long Jitter = (long)(Random.Shared.NextDouble() * PacketSettings.PpsTicks);
        LastSendTick = ParallelTickManager.ThisTickTicks + Jitter;
        if (Connection.WorkerIndex < -1)
        {
            TickId = ParallelTickManager.Register(Tick, 240);
        }
        else
        {
            TickId = ParallelTickManager.Register(Tick, 240);
            //TickId = AutoParallelTickManager.Register(Tick, WorkerIndex: Connection.WorkerIndex);
        }

        //LastSendTick = AutoParallelTickManager.ThisTickTicks + Jitter + 5000000;
        //if (Connection.WorkerIndex < -1)
        //{
        //    TickId = AutoParallelTickManager.Register(Tick, 240);
        //}
        //else
        //{
        //    TickId = AutoParallelTickManager.Register(Tick, 240);
        //    //TickId = AutoParallelTickManager.Register(Tick, WorkerIndex: Connection.WorkerIndex);
        //}
    }



    void Tick()
    {
        if (Ct.IsCancellationRequested || !Connection!.ConnectionHasFlags(NetaConnectionFlags.Connected))
        {
            Completed();
            return;
        }

        long ElapsedTicks = ParallelTickManager.ThisTickTicks - LastSendTick;
        long BaseIntervalTicks = PacketSettings.PpsTicks;

        while (ElapsedTicks >= BaseIntervalTicks)
        {
            double JitterFactor = (Random.Shared.NextDouble() - 0.5) * 0.1; // +-5%
            long IntervalTicks = BaseIntervalTicks + (long)(BaseIntervalTicks * JitterFactor);

            if (Random.Shared.NextDouble() < PacketSettings.ReliablePercentage)
            {
                Channel.SendReliableDebugPacket(DebugPktSettings);
            }
            else
            {
                Channel.SendUnreliableDebugPacket(DebugPktSettings);
            }

            ElapsedTicks -= IntervalTicks;
            LastSendTick += IntervalTicks;
        }
    }

    void Completed()
    {
        ParallelTickManager.Unregister(TickId);
        OnComplete!.Invoke(this);
    }
}