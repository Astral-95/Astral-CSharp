using Astral.Network.Transport;
using Astral.Tick;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Astral.Network.Servers;

public partial class NetaServer
{
    [DllImport("c", SetLastError = true)]
    private static extern int sendmmsg(int sockfd, Mmsghdr[] msgvec, uint vlen, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int sendmmsg(int sockfd, Mmsghdr* msgvec, uint vlen, int flags);

    struct PendingOutPacket
    {
        public PacketBuffer Data;
        public int Length;
        public SockaddrStorage Destination;
    }

    List<PendingOutPacket>[] WorkerOutgoingQueue = new List<PendingOutPacket>[ParallelTickManager.WorkerCount];

    void Initialize_Transmitter()
    {

    }

    void Start_Transmitter()
    {

    }


    //[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 16)]
    //internal readonly struct SockaddrIn
    //{
    //    public readonly ushort sin_family; // AF_INET = 2
    //    public readonly ushort sin_port;   // Big Endian
    //    public readonly uint sin_addr;     // Big Endian
    //    private readonly ulong sin_zero;   // Padding
    //
    //    public SockaddrIn(uint ipNetworkOrder, ushort portNetworkOrder)
    //    {
    //        sin_family = 2;
    //        sin_port = portNetworkOrder;
    //        sin_addr = ipNetworkOrder;
    //        sin_zero = 0;
    //    }
    //}

    [StructLayout(LayoutKind.Explicit, Size = 28)]
    internal readonly struct SockaddrStorage
    {
        [FieldOffset(0)] public readonly ushort ss_family;
        [FieldOffset(2)] public readonly ushort sin_port;

        // AF_INET (IPv4)
        [FieldOffset(4)] public readonly uint sin_addr;

        // AF_INET6 (IPv6)
        [FieldOffset(4)] public readonly uint sin6_flowinfo;
        [FieldOffset(8)] public readonly ulong sin6_addr_hi;
        [FieldOffset(16)] public readonly ulong sin6_addr_lo;
        [FieldOffset(24)] public readonly uint sin6_scope_id;

        // Pre-calculated size for msg_namelen to keep hotpath branchless
        [FieldOffset(26)] public readonly ushort Len;

        public SockaddrStorage(IPEndPoint ep)
        {
            this = default;

            var addr = ep.Address;
            // Handle dual-stack mapping: if it's v4-mapped, treat it as native v4
            bool isV4 = addr.AddressFamily == AddressFamily.InterNetwork || addr.IsIPv4MappedToIPv6;

            if (isV4)
            {
                if (addr.IsIPv4MappedToIPv6) addr = addr.MapToIPv4();

                ss_family = 2; // AF_INET
                               // sin_port is already Big Endian from HostToNetworkOrder
                sin_port = (ushort)IPAddress.HostToNetworkOrder((short)ep.Port);

                Span<byte> ipBytes = stackalloc byte[4];
                addr.TryWriteBytes(ipBytes, out _);

                // Use LittleEndian to PRESERVE the Big Endian bytes in the uint field
                sin_addr = BinaryPrimitives.ReadUInt32LittleEndian(ipBytes);

                // sin6_addr_hi/lo and flowinfo are 0 because of 'this = default'
                Len = 16;
            }
            else
            {
                ss_family = 10; // AF_INET6
                sin_port = (ushort)IPAddress.HostToNetworkOrder((short)ep.Port);
                sin6_flowinfo = 0;
                sin6_scope_id = (uint)addr.ScopeId;

                Span<byte> ipBytes = stackalloc byte[16];
                addr.TryWriteBytes(ipBytes, out _);

                // Again, use LittleEndian to preserve the network byte order in memory
                sin6_addr_hi = BinaryPrimitives.ReadUInt64LittleEndian(ipBytes[..8]);
                sin6_addr_lo = BinaryPrimitives.ReadUInt64LittleEndian(ipBytes[8..]);

                Len = 28;
            }
        }
    }

    

    // Pre-allocated per-worker scratch arrays — resize as needed, never shrink
    [ThreadStatic] private static nint _msgvecPtr;
    [ThreadStatic] private static nint _iovecsPtr;
    [ThreadStatic] private static int _scratchCapacity;



    // Pinned GC handles for the native pointers — re-pinned each flush
    // We do NOT keep long-lived pins because buffers come from a pool
    // and may be returned after send. Instead we pin for the duration
    // of the sendmmsg call and unpin immediately after.


    static unsafe void EnsureScratch(int count)
    {
        if (_scratchCapacity >= count) return;

        int newSize = Math.Max(count, Math.Max(_scratchCapacity * 2, 64));

        if (_msgvecPtr != 0) NativeMemory.Free((void*)_msgvecPtr);
        if (_iovecsPtr != 0) NativeMemory.Free((void*)_iovecsPtr);

        _msgvecPtr = (nint)NativeMemory.AllocZeroed((nuint)(newSize * sizeof(Mmsghdr)));
        _iovecsPtr = (nint)NativeMemory.AllocZeroed((nuint)(newSize * sizeof(IOVector)));
        _scratchCapacity = newSize;
    }

    /// <summary>
    /// Send all packets in the worker's outgoing queue in a single sendmmsg syscall.
    /// </summary>
    public unsafe void ParallelTick_Send(int WorkerIndex)
    {
        var Queue = WorkerOutgoingQueue[WorkerIndex];

        //var TargetIndex = NumRecvWorkers == 1 ? 0 : WorkerIndex % NumRecvWorkers;
        //var Socket = Sockets[TargetIndex];
        var Socket = Sockets[WorkerIndex];
        
        int QueueCount = Queue.Count;
        if (QueueCount == 0) return;

        // Grow scratch arrays if needed
        EnsureScratch(QueueCount);

        try
        {
            Mmsghdr* pMsgVec = (Mmsghdr*)_msgvecPtr;
            IOVector* pIoVecs = (IOVector*)_iovecsPtr;

            Span<PendingOutPacket> span = CollectionsMarshal.AsSpan(Queue);

            fixed (PendingOutPacket* pQueue = span)
            {
                for (int i = 0; i < QueueCount; i++)
                {
                    byte* pPacketData = (byte*)&pQueue[i].Data;
                    void* pPacketAddr = &pQueue[i].Destination;

                    pIoVecs[i].Base = (IntPtr)pPacketData;
                    pIoVecs[i].Length = (IntPtr)pQueue[i].Length;

                    pMsgVec[i].msg_hdr.msg_name = (IntPtr)pPacketAddr;
                    pMsgVec[i].msg_hdr.msg_namelen = span[i].Destination.Len;
                    pMsgVec[i].msg_hdr.msg_iov = (IntPtr)(&pIoVecs[i]);
                    pMsgVec[i].msg_hdr.msg_iovlen = (IntPtr)1;
                    pMsgVec[i].msg_hdr.msg_control = IntPtr.Zero;
                    pMsgVec[i].msg_hdr.msg_controllen = IntPtr.Zero;
                    pMsgVec[i].msg_hdr.msg_flags = 0;
                    pMsgVec[i].msg_len = 0;
                }

                int fd = (int)Socket.SafeHandle.DangerousGetHandle();
                int sent = sendmmsg(fd, pMsgVec, (uint)QueueCount, 0);

                if (sent < 0)
                {
                    int err = Marshal.GetLastWin32Error();
                    Logger.LogError($"sendmmsg failed: errno {err}");
                }
            }
        }
        finally
        {
            Queue.Clear();
        }
    }

    internal void EnqueueOutPacket(OutPacket Packet, SockaddrStorage Addr, int WorkerIndex)
    {
        var Queue = WorkerOutgoingQueue[WorkerIndex];
        Queue.Add(default); // Add an empty slot

        ref PendingOutPacket Pending = ref CollectionsMarshal.AsSpan(Queue)[Queue.Count - 1];

        // Copy data into the inline array
        Packet.GetBuffer()[..Packet.Pos].CopyTo(MemoryMarshal.CreateSpan(ref Pending.Data[0], NetaConsts.BufferMaxSizeBytes));
        Pending.Length = Packet.Pos;
        Pending.Destination = Addr;
    }

    private static ushort BSwap16(ushort v) =>
            (ushort)((v >> 8) | (v << 8));

    private static uint BSwap32(uint v) =>
        ((v & 0x000000FFu) << 24) |
        ((v & 0x0000FF00u) << 8) |
        ((v & 0x00FF0000u) >> 8) |
        ((v & 0xFF000000u) >> 24);
}