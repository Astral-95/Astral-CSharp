using Astral.Diagnostics;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Astral;

public static partial class Context
{
    static public int LogicalProcessorCount { get; private set; }

    static Context()
    {
        LogicalProcessorCount = Math.Max(1, Environment.ProcessorCount);
        //if (GCSettings.IsServerGC)
        //{
        //    GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        //}
        //else
        //{
        //    GCSettings.LatencyMode = GCLatencyMode.LowLatency;
        //}
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformation(IntPtr Buffer, ref int ReturnLength);

    public enum LogicalProcessorRelationship
    {
        RelationProcessorCore = 0,
        RelationNumaNode = 1,
        RelationCache = 2,
        RelationProcessorPackage = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SystemLogicalProcessorInformation
    {
        public UIntPtr ProcessorMask;
        public LogicalProcessorRelationship Relationship;
        // This is a union in C++, but for SMT check we only need the base size
        public long Reserved1;
        public long Reserved2;
    }

    public static bool IsSmtEnabledWindows()
    {
        int ReturnLength = 0;
        // Call once to get the required buffer size
        GetLogicalProcessorInformation(IntPtr.Zero, ref ReturnLength);

        if (ReturnLength == 0) return false;

        IntPtr Buffer = Marshal.AllocHGlobal(ReturnLength);
        try
        {
            if (GetLogicalProcessorInformation(Buffer, ref ReturnLength))
            {
                int Size = Marshal.SizeOf<SystemLogicalProcessorInformation>();
                int StructCount = ReturnLength / Size;
                int PhysicalCoreCount = 0;

                for (int i = 0; i < StructCount; i++)
                {
                    IntPtr CurrentPtr = Buffer + (i * Size);
                    var Info = Marshal.PtrToStructure<SystemLogicalProcessorInformation>(CurrentPtr);

                    // Each "RelationProcessorCore" entry represents one physical silicon core
                    if (Info.Relationship == LogicalProcessorRelationship.RelationProcessorCore)
                    {
                        PhysicalCoreCount++;
                    }
                }

                // Compare against total logical processors
                return Environment.ProcessorCount > PhysicalCoreCount;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(Buffer);
        }

        return false;
    }



    [DllImport("libc", SetLastError = true)]
    private static extern int sched_setaffinity(int pid, IntPtr cpusetsize, long[] mask);

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentThreadId();

    public static void SetThreadAffinity(int coreId)
    {
        // 1. Hard-bind to the specific CPU core
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // 16 longs * 8 bytes = 128 bytes (matches __CPU_SETSIZE of 1024 bits)
            long[] mask = new long[16];

            int blockIndex = coreId / 64;
            int bitOffset = coreId % 64;
            mask[blockIndex] = 1L << bitOffset;

            // Use 128 as the size. This is the "magic number" for Debian/Ubuntu libc.
            int result = sched_setaffinity(0, (IntPtr)128, mask);

            if (result != 0)
            {
                int error = Marshal.GetLastWin32Error();
                // If you get 22 (EINVAL), the kernel thinks 128 is the wrong size.
                // If you get 0, it worked!
                Console.WriteLine($"Affinity Result: {result}, Error: {error}");
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            IntPtr mask = new IntPtr(1L << coreId);
            int managedId = GetCurrentThreadId();

            foreach (ProcessThread pt in Process.GetCurrentProcess().Threads)
            {
                if (pt.Id == managedId)
                {
                    pt.ProcessorAffinity = mask;
                    break;
                }
            }
        }

        // 2. Set Highest Priority
        Thread.CurrentThread.Priority = ThreadPriority.Highest;
    }
}