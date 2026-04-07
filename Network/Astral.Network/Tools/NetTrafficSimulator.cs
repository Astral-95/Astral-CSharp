using Astral.Tick;
using System.Collections.Concurrent;

namespace Astral.Network.Tools;

public static class NetTrafficSimulator
{
    private static readonly Random Rand = new Random();
    private static readonly ConcurrentQueue<(Action Action, long ReleaseTicks)> Queue = new();

    private static readonly double TickFrequency = (double)Context.ClockFrequency; // ticks per second

    // --- Baseline ---
    public static float BaselineDelayMs = 60f;
    public static float BaselineJitterMs = 10f;
    public static float BaselineLossChance = 0.005f; // 0.5%

    // --- Block configuration ---
    public static int BlockDurationSeconds = 60;

    public static int BlockDurationSecondsMin = 50;
    public static int BlockDurationSecondsMax = 70;

    // --- Event probabilities per block (percent) ---
    public static int MinorChance = 70;
    public static int MajorChance = 28;
    public static int CatastrophicChance = 2;

    // --- Spike ranges ---
    public static (float Min, float Max) MinorDelay = (40, 60);
    public static (float Min, float Max) MinorLoss = (2, 5);
    public static (int Min, int Max) MinorDuration = (4, 5);

    public static (float Min, float Max) MajorDelay = (75, 125);
    public static (float Min, float Max) MajorLoss = (5, 15);
    public static (int Min, int Max) MajorDuration = (6, 12);

    public static (float Min, float Max) CatastrophicDelay = (150, 250);
    public static float CatastrophicLoss = 50;
    public static (int Min, int Max) CatastrophicDuration = (5, 10);

    private static float CurrentDelayMs = BaselineDelayMs;
    private static float CurrentLossChance = BaselineLossChance;
    private static float CurrentJitterMs = BaselineJitterMs;


    public static void Start(CancellationToken Token = default)
    {
        ParallelTickManager.Register(Tick);

        Task.Run(async () =>
        {
            while (!Token.IsCancellationRequested)
            {
                int RandomDelay = Rand.Next(BlockDurationSeconds) * 1000;
                await Task.Delay(RandomDelay, Token);

                int EventRoll = Rand.Next(100);
                if (EventRoll < MinorChance)
                    ApplySpike(MinorDelay, MinorLoss, MinorDuration, "Minor");
                else if (EventRoll < MinorChance + MajorChance)
                    ApplySpike(MajorDelay, MajorLoss, MajorDuration, "Major");
                else
                    ApplySpike(CatastrophicDelay, (CatastrophicLoss, CatastrophicLoss), CatastrophicDuration, "Catastrophic");

                // Wait remaining block
                //int Remaining = BlockDurationSeconds * 1000 - RandomDelay;
                //if (Remaining > 0)
                //	await Task.Delay(Remaining, Token);
            }
        }, Token);
    }

    //public static void Stop()
    //{
    //
    //}

    // Call this in your main loop
    public static void Tick()
    {
        long TicksNow = ParallelTickManager.ThisTickTicks;

        while (Queue.TryPeek(out var Tuple))
        {
            if (Tuple.ReleaseTicks > TicksNow) continue;

            Queue.TryDequeue(out _);
            Tuple.Action.Invoke();
        }
    }

    public static void Receive(Action Action)
    {
        if (Rand.NextDouble() < CurrentLossChance) return;

        // Compute total delay in ms including jitter
        double DelayMs = CurrentDelayMs + (Rand.NextDouble() * 2 - 1) * CurrentJitterMs;
        long ReleaseTicks = Context.Ticks + (long)(DelayMs / 1000.0 * TickFrequency);

        Queue.Enqueue((Action, ReleaseTicks));
    }

    private static void ApplySpike((float Min, float Max) DelayRange, (float Min, float Max) LossRange, (int Min, int Max) DurationRange, string Name)
    {
        CurrentDelayMs = (float)(Rand.NextDouble() * (DelayRange.Max - DelayRange.Min) + DelayRange.Min);
        CurrentLossChance = (float)(Rand.NextDouble() * (LossRange.Max - LossRange.Min) + LossRange.Min) / 100f;

        int DurationSec = Rand.Next(DurationRange.Min, DurationRange.Max + 1);

        Console.WriteLine($"{DateTime.Now}: {Name} spike -> Delay={CurrentDelayMs}ms, Loss={CurrentLossChance * 100}%, Duration={DurationSec}s");

        // Schedule returning to baseline
        Task.Delay(DurationSec * 1000).ContinueWith(_ =>
        {
            CurrentDelayMs = BaselineDelayMs;
            CurrentLossChance = BaselineLossChance;
            CurrentJitterMs = BaselineJitterMs;
        });
    }
}