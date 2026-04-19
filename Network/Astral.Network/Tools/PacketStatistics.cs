using Astral.Tick;
using System.Diagnostics;

namespace Astral.Network.Tools;

public class PacketStatistics
{
    private static readonly long TickFrequency = Context.ClockFrequency;

    // High-level values stored in the connection state

    // Milliseconds * Context.ClockFrequency / 1000.0)
    long PrivateSmoothedRttTicks = (long)(100.0 * (TickFrequency / 1000.0)); // SRTT
    public long SmoothedRttTicks { get => Volatile.Read(ref PrivateSmoothedRttTicks); }
    public long RttVarianceTicks { get; private set; } // RTTVAR

    // Milliseconds * Context.ClockFrequency / 1000.0)
    long RetransmissionTimeoutTicks = (long)(500.0 * (TickFrequency / 1000.0)); // RTO

    // Milliseconds * Context.ClockFrequency / 1000.0)
    public static readonly long MinRetransmissionTimeoutTicks = (long)(100.0 * (TickFrequency / 1000.0));
    public static readonly long MaxRetransmissionTimeoutTicks = (long)(5000.0 * (TickFrequency / 1000.0));

    // --- Central Adjustment Point for Smoothing ---
    // Change this value to set the smoothing level (alpha = 1 / 2^AlphaShift).
    // 3: Standard TCP-like smoothing (alpha = 1/8)
    // 5: Very smooth, high jitter resistance (alpha = 1/32)
    const int AlphaShift = 4;

    private const int BetaShift = 2;  // 1/4
    private const int RtoMultiplier = 4; // Multiplier for RTTVAR

    // Automatically derive the scaling factor (8, 16, or 32)
    const long AlphaScale = 1L << AlphaShift;

    // Automatically derive the multiplier for the old SRTT (7, 15, or 31)
    const long AlphaOldMultiplier = AlphaScale - 1;

    // 100 * (Ticks Per Second / 1000) = Ticks in 0.1 seconds
    private static readonly long MinRtoTicks = 100 * (TickFrequency / 1000);

    // 5000ms * (Ticks Per Second / 1000) = Ticks in 5 seconds
    private static readonly long MaxRtoTicks = 5000 * (TickFrequency / 1000);


    // (long)(Ms * Context.ClockFrequency / 1000.0));
    public static readonly long PingsSendTicks = (long)(1000.0 * (TickFrequency / 1000.0));
    public static readonly int PingsSendDelay = 1000;
    public Int64 LastReceiveTicks { get; set; } = Int64.MaxValue;
#if DEBUG
    public const int TimeoutSeconds = 60;
#else
        public const int TimeoutSeconds = 10;
#endif


    internal UInt64 AppIn = 0;
    internal UInt64 AppOut = 0;

    internal UInt64 TotalIn = 0;
    internal UInt64 TotalOut = 0;

    internal UInt64 SimulatedLoss = 0;
    internal UInt64 Retransmitted = 0;

    public long GetSmoothedRttTicks() => Volatile.Read(ref PrivateSmoothedRttTicks);
    //public double GetSmoothedRttMilliSeconds() => (double)SmoothedRttTicks * 1000.0 / TickFrequency;
    public double GetSmoothedRttMilliSeconds() => SmoothedRttTicks * 1000.0 / TickFrequency;

    //public long GetRetransmissionTimeoutTicks() => Interlocked.Read(ref RetransmissionTimeoutTicks);
    public long GetRetransmissionTimeoutTicks() => Volatile.Read(ref RetransmissionTimeoutTicks);

    internal void UpdateRtt(long RttSampleTicks, long RemoteProcessMilliseconds)
    {
        var CurrRTT = Volatile.Read(ref PrivateSmoothedRttTicks);
        var NewRTT = (Int64)(CurrRTT * 0.75 + RttSampleTicks * 0.25);
        Interlocked.Exchange(ref PrivateSmoothedRttTicks, NewRTT);
        var NewRTO = Math.Min(MaxRtoTicks, (long)(NewRTT * 2 + (TickFrequency * 0.100)));
        Interlocked.Exchange(ref RetransmissionTimeoutTicks, NewRTO);
    }

    [Conditional("NETA_PKT_STATS")]
    public void ResetCounters()
    {
        Interlocked.Exchange(ref AppIn, 0);
        Interlocked.Exchange(ref AppOut, 0);
        Interlocked.Exchange(ref TotalIn, 0);
        Interlocked.Exchange(ref TotalOut, 0);
        Interlocked.Exchange(ref SimulatedLoss, 0);
        Interlocked.Exchange(ref Retransmitted, 0);
    }

    [Conditional("NETA_PKT_STATS")]
    internal void IncrementAppIn() { Interlocked.Increment(ref AppIn); }
    [Conditional("NETA_PKT_STATS")]

    internal void IncrementAppOut() { Interlocked.Increment(ref AppOut); }

    [Conditional("NETA_PKT_STATS")]
    internal void IncrementInPacket()
    {
        Interlocked.Increment(ref TotalIn);
        LastReceiveTicks = ParallelTickManager.ThisTickTicks;
    }
    [Conditional("NETA_PKT_STATS")]
    internal void IncrementOutPacket() => Interlocked.Increment(ref TotalOut);
    [Conditional("NETA_PKT_STATS")]
    internal void IncrementRetransmitted() { Interlocked.Increment(ref Retransmitted); }




    public UInt64 GetTotalIn() { return TotalIn; }
    public UInt64 GetTotalOut() { return TotalOut; }

    public UInt64 GetSimulatedLoss() { return SimulatedLoss; }


    public void Reset()
    {
        Interlocked.Exchange(ref AppIn, 0);
        Interlocked.Exchange(ref AppOut, 0);

        Interlocked.Exchange(ref TotalIn, 0);
        Interlocked.Exchange(ref TotalOut, 0);

        Interlocked.Exchange(ref SimulatedLoss, 0);
        Interlocked.Exchange(ref Retransmitted, 0);
    }
}


public class PacketStatisticsSnapshot
{
    public bool InPacketLossEnabled { get; set; } = false;

    public double InPacketLossPercentage { get; set; } = 0.0;



    public UInt64 AppIn { get; set; } = 0;
    public UInt64 AppOut { get; set; } = 0;

    public UInt64 TotalIn { get; set; } = 0;
    public UInt64 TotalOut { get; set; } = 0;

    public UInt64 SimulatedLoss { get; set; } = 0;
    public UInt64 Retransmitted { get; set; } = 0;

    // SmoothedRttTicks
    public long SrttTicks { get; set; } = 0;

    // SmoothedRttMilliseconds
    public double SrttMilliseconds { get; set; } = 0;

    public List<Exception> SendExceptions { get; set; } = new List<Exception>(64);
    public List<Exception> ResendExceptions { get; set; } = new List<Exception>(64);
    public List<Exception> AcksOnlySendExcetpions { get; set; } = new List<Exception>(64);

    public static PacketStatisticsSnapshot From(PacketStatistics Stats)
    {
        var Snapshot = new PacketStatisticsSnapshot();

        Snapshot.AppIn = Interlocked.Read(ref Stats.AppIn);
        Snapshot.AppOut = Interlocked.Read(ref Stats.AppOut);

        Snapshot.TotalIn = Interlocked.Read(ref Stats.TotalIn);
        Snapshot.TotalOut = Interlocked.Read(ref Stats.TotalOut);
        Snapshot.Retransmitted = Interlocked.Read(ref Stats.Retransmitted);
        Snapshot.SimulatedLoss = Interlocked.Read(ref Stats.SimulatedLoss);

        Snapshot.SrttMilliseconds = Stats.GetSmoothedRttMilliSeconds();
        return Snapshot;
    }

    public void Add(PacketStatistics Stats)
    {
        AppIn += Interlocked.Read(ref Stats.AppIn);
        AppOut += Interlocked.Read(ref Stats.AppOut);

        TotalIn += Interlocked.Read(ref Stats.TotalIn);
        TotalOut += Interlocked.Read(ref Stats.TotalOut);
        Retransmitted += Interlocked.Read(ref Stats.Retransmitted);
        SimulatedLoss += Interlocked.Read(ref Stats.SimulatedLoss);

        //RttMs = Stats.RttVarMs;
        //AvgPacketProcessTimeMilliSecs = Stats.GetPacketsPerSecond();
        SrttMilliseconds = (Stats.GetSmoothedRttMilliSeconds() + Stats.GetSmoothedRttMilliSeconds()) * 0.5f;
    }

    public void Reset()
    {
        AppIn = 0;
        AppOut = 0;
        TotalIn = 0;
        TotalOut = 0;
        Retransmitted = 0;
        SimulatedLoss = 0;
        SrttMilliseconds = 0.0;
    }


    public double GetInPPS(double ElapsedSeconds) => TotalIn / ElapsedSeconds;
    public double GetOutPPS(double ElapsedSeconds) => TotalOut / ElapsedSeconds;
    public double GetAppInPPS(double ElapsedSeconds) => AppIn / ElapsedSeconds;
    public double GetAppOutPPS(double ElapsedSeconds) => AppOut / ElapsedSeconds;
}