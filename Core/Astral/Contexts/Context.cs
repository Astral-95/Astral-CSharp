using Astral.Diagnostics;
using System.Runtime.InteropServices;

namespace Astral;

public static partial class Context
{
    static public bool IsSmtEnabled { get; private set; }
    static public int LogicalProcessorCount { get; private set; }

    static Context()
    {
        IsSmtEnabled = IsSmtActive();
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


    public static bool IsSmtEnabledLinux()
    {
        // Check 'siblings' vs 'cpu cores' in /proc/cpuinfo
        // If siblings > cpu cores, SMT is on.
        var Lines = File.ReadAllLines("/proc/cpuinfo");
        var Siblings = Lines.FirstOrDefault(x => x.Contains("siblings"))?.Split(':').Last().Trim();
        var Cores = Lines.FirstOrDefault(x => x.Contains("cpu cores"))?.Split(':').Last().Trim();

        if (int.TryParse(Siblings, out int S) && int.TryParse(Cores, out int C))
        {
            return S > C;
        }
        return false;
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


    public static bool IsSmtActive()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return IsSmtEnabledWindows();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return IsSmtEnabledLinux();
        }

        Guard.Fail("OS not supported.");
        return false;
    }
}