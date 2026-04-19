using Astral.Logging;
using Astral.Network.Toolkit;
using Astral.Network.Transport;
using Astral.Tick;
using System.Buffers.Binary;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static Astral.Network.Servers.NetaServer;

// TODO: Congestion / RTT-Based Window
// Drop unreliable packets if we start resending, keep dropping more and more until resends stop
// When resends stops, Wait a bit then start slowly ramping up unreliable sending
namespace Astral.Network.Sockets;

//public enum NetaSocketFlags
//{
//	None = 0,
//	Pending = 1 << 1,
//	Connected = 1 << 2,
//	Closed = 1 << 3,
//	Server = 1 << 4,
//	Handshaking = 1 << 6,
//	Reconnecting = 1 << 7,
//	TimedOut = 1 << 8,
//	Dormant = 1 << 9,
//	Error = 1 << 10,
//	VoiceEnabled = 1 << 11,
//	SimulatedLag = 1 << 12,
//}

[System.Runtime.CompilerServices.InlineArray(NetaConsts.BufferMaxSizeBytes)]
public struct PacketBuffer
{
    private byte _;
}

[StructLayout(LayoutKind.Sequential)]
public struct IOVector
{
    public IntPtr Base;   // pointer to buffer
    public IntPtr Length; // length of buffer
}

[StructLayout(LayoutKind.Sequential)]
public struct MsgHdr
{
    public IntPtr msg_name;       // pointer to sockaddr
    public int msg_namelen;    // size of sockaddr
    public IntPtr msg_iov;        // pointer to IOVector array
    public IntPtr msg_iovlen;     // number of IOVectors (1 per packet)
    public IntPtr msg_control;    // 0
    public IntPtr msg_controllen; // 0
    public int msg_flags;      // 0
}

[StructLayout(LayoutKind.Sequential)]
public struct Mmsghdr
{
    public MsgHdr msg_hdr;
    public uint msg_len; // filled in by kernel on return
}

[StructLayout(LayoutKind.Sequential)]
public struct TimeSpec
{
    public long tv_sec;
    public long tv_nsec;
}



[StructLayout(LayoutKind.Explicit, Size = 28)]
public readonly struct SocketAddrStorage
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


    public SocketAddrStorage(ref NetaAddress Addr)
    {
        this = default;

        // 1. Port: Must be Big-Endian for the struct
        sin_port = BitConverter.IsLittleEndian
            ? BinaryPrimitives.ReverseEndianness(Addr.Port)
            : Addr.Port;

        if (Addr.Address <= uint.MaxValue)
        {
            // IPv4 Logic
            ss_family = 2; // AF_INET

            // Ensure the 4 bytes of the uint are in Network Byte Order
            uint ipv4 = (uint)Addr.Address;
            sin_addr = BitConverter.IsLittleEndian
                ? BinaryPrimitives.ReverseEndianness(ipv4)
                : ipv4;

            Len = 16;
        }
        else
        {
            // IPv6 Logic
            ss_family = 10; // AF_INET6

            // Split the UInt128. 
            // We assume 'address' was constructed with the High 64 bits 
            // containing the start of the IP.
            ulong hi = (ulong)(Addr.Address >> 64);
            ulong lo = (ulong)(Addr.Address & ulong.MaxValue);

            // If your system is Little-Endian, and you want 'hi' to represent 
            // the FIRST 8 bytes of the network address, they must stay Big-Endian.
            // If you used BinaryPrimitives.ReadUInt64BigEndian to GET the UInt128,
            // then the bytes are already 'swapped' into a math-friendly format.
            // To put them BACK in the struct, we reverse them.
            sin6_addr_hi = BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness(hi) : hi;
            sin6_addr_lo = BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness(lo) : lo;

            Len = 28;
        }
    }

    public SocketAddrStorage(IPEndPoint ep)
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


public struct PendingOutPacket
{
    public PacketBuffer Data;
    public int Length;
    public SocketAddrStorage Destination;
}


public unsafe class NetaSocket : Socket
{
#if LINUX
    static class Native
    {
        public const int SHUT_RD = 0;
        public const int SHUT_WR = 1;
        public const int SHUT_RDWR = 2;

        [DllImport("libc", SetLastError = true)]
        public static extern int shutdown(nint sockfd, int how);
    }
#endif


    [DllImport("libc", SetLastError = true)]
    public static extern int recvmmsg(int sockfd, Mmsghdr* msgvec, uint vlen, int flags, TimeSpec* timeout);

    [DllImport("libc", SetLastError = true)]
    public static extern int sendmmsg(int sockfd, Mmsghdr* msgvec, uint vlen, int flags);
    public int SocketFd { get; private set; }


    bool RioEnabled = false;


    void Init()
    {
        SocketFd = (int)SafeHandle.DangerousGetHandle();
    }


    //
    // Summary:
    //     Initializes a new instance of the System.Net.Sockets.Socket class for the specified
    //     socket handle.
    //
    // Parameters:
    //   handle:
    //     The socket handle for the socket that the System.Net.Sockets.Socket object will
    //     encapsulate.
    //
    // Exceptions:
    //   T:System.ArgumentNullException:
    //     handle is null.
    //
    //   T:System.ArgumentException:
    //     handle is invalid.
    //
    //   T:System.Net.Sockets.SocketException:
    //     handle is not a socket or information about the socket could not be accessed.
    public NetaSocket(SafeSocketHandle Handle) : base(Handle)
    {
        Init();
    }

    //
    // Summary:
    //     Initializes a new instance of the System.Net.Sockets.Socket class using the specified
    //     value returned from System.Net.Sockets.Socket.DuplicateAndClose(System.Int32).
    //
    //
    // Parameters:
    //   socketInformation:
    //     The socket information returned by System.Net.Sockets.Socket.DuplicateAndClose(System.Int32).
    [SupportedOSPlatform("windows")]
    public NetaSocket(SocketInformation Info) : base(Info)
    {
        Init();
    }

    //
    // Summary:
    //     Initializes a new instance of the System.Net.Sockets.Socket class using the specified
    //     socket type and protocol. If the operating system supports IPv6, this constructor
    //     creates a dual-mode socket; otherwise, it creates an IPv4 socket.
    //
    // Parameters:
    //   SocketType:
    //     One of the System.Net.Sockets.SocketType values.
    //
    //   ProtocolType:
    //     One of the System.Net.Sockets.ProtocolType values.
    //
    // Exceptions:
    //   T:System.Net.Sockets.SocketException:
    //     The combination of socketType and protocolType results in an invalid socket.
    public NetaSocket(SocketType SocketType, ProtocolType ProtocolType) : base(SocketType, ProtocolType)
    {
        Init();
    }

    //
    // Summary:
    //     Initializes a new instance of the System.Net.Sockets.Socket class using the specified
    //     address family, socket type and protocol.
    //
    // Parameters:
    //   AddressFamily:
    //     One of the System.Net.Sockets.AddressFamily values.
    //
    //   SocketType:
    //     One of the System.Net.Sockets.SocketType values.
    //
    //   ProtocolType:
    //     One of the System.Net.Sockets.ProtocolType values.
    //
    // Exceptions:
    //   T:System.Net.Sockets.SocketException:
    //     The combination of addressFamily, socketType, and protocolType results in an
    //     invalid socket.

    public NetaSocket(AddressFamily AddressFamily, SocketType SocketType, ProtocolType ProtocolType) : base(AddressFamily, SocketType, ProtocolType)
    {
        Init();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Recvmmsg(Mmsghdr* msgvec, uint vlen, int flags, TimeSpec* timeout) => recvmmsg(SocketFd, msgvec, vlen, flags, timeout);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int SendMultiMessage(Mmsghdr* msgvec, uint vlen, int flags) => sendmmsg(SocketFd, msgvec, vlen, flags);




    //public void UseRio()
    //{
    //    // 1. Load the RIO Function Table via WSAIoctl (Simplified here)
    //    _rio = LoadRioFunctionTable(socketHandle);
    //
    //    // 2. Register a large block of memory (The "Registered Buffer")
    //    // This memory is locked into physical RAM by the kernel.
    //    int bufferSize = 1024 * 1024; // 1MB
    //    IntPtr pBuffer = Marshal.AllocHGlobal(bufferSize);
    //    _bufferId = RIORegisterBuffer(pBuffer, (uint)bufferSize);
    //
    //    // 3. Create a Completion Queue (CQ)
    //    // This is where the results of ALL sockets end up.
    //    _completionQueue = RIOCreateCompletionQueue(1000, IntPtr.Zero);
    //
    //    // 4. Create a Request Queue (RQ) for a specific socket
    //    _requestQueue = RIOCreateRequestQueue(socketHandle, 100, 1, 100, 1, _completionQueue, _completionQueue, IntPtr.Zero);
    //}



    //
    // Summary:
    //     Releases the unmanaged resources used by the System.Net.Sockets.Socket, and optionally
    //     disposes of the managed resources.
    //
    // Parameters:
    //   disposing:
    //     true to release both managed and unmanaged resources; false to releases only
    //     unmanaged resources.
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
#if LINUX
        if (disposing) Native.shutdown(Handle, Native.SHUT_RDWR);
#endif
    }
}