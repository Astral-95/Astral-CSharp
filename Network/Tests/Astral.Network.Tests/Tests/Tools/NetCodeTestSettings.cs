
namespace Astral.Network.Tests.Tools;

public class NetCodeTestPacketSettings
{
    public double ReliablePercentage { get; set; } = 0.2;
    public int MinPPS { get; set; } = 10;
    public int MaxPPS { get; set; } = 100;
    public int Pps
    {
        get
        {
            if (MinPPS > MaxPPS)
            {
                MinPPS = MaxPPS;
            }
            return Random.Shared.Next(MinPPS, MaxPPS);
        }
    }
    public long PpsTicks
    {
        get
        {
            if (Pps == 0) return 0;
            return Context.ClockFrequency / Pps;
        }
    }


    public bool FixedBodySize { get; set; } = true;
    public int FixedBodySizeBytes { get; set; } = 128;
    public int MinBodySizeBytes { get; set; } = 64;
    public int MaxBodySizeBytes { get; set; } = 128;

    public NetCodeTestPacketSettings() { }
}

public class NetCodeTestSettings
{
    public int NumRuns { get; set; } = 1;
    public int NumClients { get; set; } = 1;
    public int DurationSeconds { get; set; } = 60;

    public bool ServerEnabled { get; set; } = true;
    public bool ClientsEnabled { get; set; } = true;
    public string ServerAddress { get; set; } = "127.0.0.1:5000";
    public string ClientsServerAddress { get; set; } = "127.0.0.1:5000";

    public NetCodeTestPacketSettings ServerPackets { get; set; }
    public NetCodeTestPacketSettings ClientPackets { get; set; }

    public NetCodeTestSettings()
    {
        ServerPackets = new();
        ClientPackets = new();
    }
}