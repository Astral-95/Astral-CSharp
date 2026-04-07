using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

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


public class NetaSocket : Socket
{
    static class Native
    {
        public const int SHUT_RD = 0;
        public const int SHUT_WR = 1;
        public const int SHUT_RDWR = 2;

        [DllImport("libc", SetLastError = true)]
        public static extern int shutdown(nint sockfd, int how);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RIO_EXTENSION_FUNCTION_TABLE
    {
        public uint cbSize;
        public IntPtr RIOReceive;
        public IntPtr RIOReceiveEx;
        public IntPtr RIOSend;
        public IntPtr RIOSendEx;
        public IntPtr RIOCloseCompletionQueue;
        public IntPtr RIOCreateCompletionQueue;
        public IntPtr RIOCreateRequestQueue;
        public IntPtr RIODequeueCompletion;
        public IntPtr RIODeregisterBuffer;
        public IntPtr RIONotify;
        public IntPtr RIORegisterBuffer;
        public IntPtr RIOResizeCompletionQueue;
        public IntPtr RIOResizeRequestQueue;
    }

    private RIO_EXTENSION_FUNCTION_TABLE _rio;
    private IntPtr _completionQueue;
    private IntPtr _requestQueue;
    private IntPtr _bufferId;


    bool RioEnabled = false;


    void Init()
    {
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