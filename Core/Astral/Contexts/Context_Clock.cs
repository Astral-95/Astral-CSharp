using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Astral;

public static partial class Context
{
    public static class Clock
    {
        internal static long Frequency = Stopwatch.Frequency;
        private static long BaseVirtualTicks = 0;
        private static long LastHardwareTimestamp = Stopwatch.GetTimestamp();
        private static double IMultiplier = 1.0;
        private static System.Threading.Timer? MultiplierLerpTimer;
        private static double MultiplierTransitionDurationSeconds = 2.5; // Default to 1s
        private static bool IIsPaused = false;

        private static readonly object LockObj = new object();
        private static readonly double TickToMicro = 1_000_000.0 / Stopwatch.Frequency;

        public static readonly double TicksToMs = 1000.0 / Frequency;

        public static long Ticks
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                // Capture base first. 
                long baseV = Interlocked.Read(ref BaseVirtualTicks);

                if (Volatile.Read(ref IIsPaused))
                    return baseV;

                long now = Stopwatch.GetTimestamp();
                double m = Volatile.Read(ref IMultiplier);
                long lastHdt = Interlocked.Read(ref LastHardwareTimestamp);

                // The Math: (Time Passed * Speed) + Saved Progress
                return baseV + (long)((now - lastHdt) * m);
            }
        }

        public static long Micro => (long)(Ticks * TickToMicro);

        public static double Multiplier
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Volatile.Read(ref IMultiplier);
            set
            {
                //lock (LockObj)
                //{
                //    if (Math.Abs(_multiplier - value) < double.Epsilon) return;
                //    SyncInternal();
                //    Volatile.Write(ref _multiplier, value);
                //}

                lock (LockObj)
                {
                    MultiplierLerpTimer?.Dispose();

                    double startVal = IMultiplier;
                    double targetVal = value;
                    if (Math.Abs(startVal - targetVal) < 1e-9) return;

                    const int hz = 40;
                    int currentStep = 0;

                    int totalSteps = (int)(MultiplierTransitionDurationSeconds * hz);
                    int intervalMs = 1000 / hz;

                    MultiplierLerpTimer = new System.Threading.Timer(_ =>
                    {
                        lock (LockObj)
                        {
                            // 1. Sync the clock progress at the OLD speed
                            long now = Stopwatch.GetTimestamp();
                            long deltaHdt = now - LastHardwareTimestamp;
                            BaseVirtualTicks += (long)(deltaHdt * IMultiplier);
                            LastHardwareTimestamp = now;

                            // 2. Step the lerp
                            currentStep++;
                            double t = (double)currentStep / totalSteps;
                            IMultiplier = startVal * Math.Pow(targetVal / startVal, t);

                            // 3. Cleanup
                            if (currentStep >= totalSteps)
                            {
                                IMultiplier = targetVal;
                                MultiplierLerpTimer?.Dispose();
                                MultiplierLerpTimer = null;
                            }
                        }
                    }, null, 0, intervalMs);
                }
            }
        }


        public static bool IsPaused
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Volatile.Read(ref IIsPaused);
        }

        public static void Pause()
        {
            lock (LockObj)
            {
                if (IIsPaused) return;
                SyncInternal();
                Volatile.Write(ref IIsPaused, true);
            }
        }

        public static void Resume()
        {
            lock (LockObj)
            {
                if (!IIsPaused) return;
                // Simply update the 'anchor' so we don't count the time spent paused
                Interlocked.Exchange(ref LastHardwareTimestamp, Stopwatch.GetTimestamp());
                Volatile.Write(ref IIsPaused, false);
            }
        }

        private static void SyncInternal()
        {
            long now = Stopwatch.GetTimestamp();
            double m = Volatile.Read(ref IMultiplier);
            long lastHdt = Interlocked.Read(ref LastHardwareTimestamp);

            long deltaHardware = now - lastHdt;
            long virtualDelta = (long)(deltaHardware * m);

            Interlocked.Add(ref BaseVirtualTicks, virtualDelta);
            Interlocked.Exchange(ref LastHardwareTimestamp, now);
        }
    }


    public static double TickMultiplier 
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Clock.Multiplier;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Clock.Multiplier = value; 
    }
    public static long Ticks 
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Clock.Ticks;
    }
    public static long ClockFrequency 
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Clock.Frequency;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void PauseClock() => Clock.Pause();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ResumeClock() => Clock.Resume();
}