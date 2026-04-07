using Astral.Logging;
using Astral.Network.Sockets;
using Astral.Network.Toolkit;
using Astral.Network.Tools;
using Astral.Network.Transport;
using Astral.Tick;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Astral.Exceptions;

namespace Astral.Network.Servers;

public partial class NetaServer
{
    [DllImport("libc", SetLastError = true)]
    static unsafe extern int recvmmsg(int sockfd, Mmsghdr* msgvec, uint vlen, int flags, TimeSpec* timeout);

    [StructLayout(LayoutKind.Sequential)]
    struct TimeSpec
    {
        public long tv_sec;
        public long tv_nsec;
    }


    int NumRecvWorkers = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? ParallelTickManager.WorkerCount : 1; // Windows/macOS get single socket, no true multi-socket benefit
    Thread[] ReceiveWorkers;

    //AtomicQueue<(Socket Socket, PooledInPacket Packet, EndPointKey EndPointKey)>[] IncomingQueues;
#if LINUX
    Queue<(NetaSocket Socket, PooledInPacket Packet, EndPointKey EndPointKey)>[] IncomingQueues;
#else
    ConcurrentQueue<(NetaSocket Socket, PooledInPacket Packet, EndPointKey EndPointKey)>[] IncomingQueues;
#endif



    private unsafe EndPointKey ExtractKey(ref SockaddrStorage Storage)
    {
        ushort port = (ushort)IPAddress.NetworkToHostOrder((short)Storage.sin_port);
        Span<byte> buffer = stackalloc byte[16];

        if (Storage.ss_family == 2)
        {
            // Write IPv4 to the FRONT so Slice(0, 4) works
            BinaryPrimitives.WriteUInt32BigEndian(buffer, Storage.sin_addr);
        }
        else
        {
            // Copy the 16 bytes exactly as they are in the struct
            fixed (ulong* p = &Storage.sin6_addr_hi)
            {
                new ReadOnlySpan<byte>(p, 16).CopyTo(buffer);
            }
        }

        // Read it as Big Endian so WriteUInt128BigEndian puts it back exactly the same
        UInt128 addr = BinaryPrimitives.ReadUInt128BigEndian(buffer);
        return new EndPointKey(addr, port);
    }



    void Initialize_Receiver(IPEndPoint LocalEndPoint, int RecBufferSize = 2 * 1024 * 1024, int SendBufferSize = 2 * 1024 * 1024)
    {
#if WINDOWS
        IncomingQueues = new ConcurrentQueue<(NetaSocket Socket, PooledInPacket Packet, EndPointKey EndPointKey)>[ParallelTickManager.WorkerCount];

        var S = new NetaSocket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
        S.DualMode = true; // handles v4 and v6
        SetReusePort(S);
        S.ReceiveBufferSize = RecBufferSize;
        S.SendBufferSize = SendBufferSize;
        S.Bind(LocalEndPoint);
        Sockets.Add(S);

        for (int i = 0; i < ParallelTickManager.WorkerCount; i++)
        {
            IncomingQueues[i] = new ConcurrentQueue<(NetaSocket Socket, PooledInPacket Packet, EndPointKey EndPointKey)>();
        }
#elif LINUX
        IncomingQueues = new Queue<(NetaSocket Socket, PooledInPacket Packet, EndPointKey EndPointKey)>[NumRecvWorkers];

        ReceiveWorkers = new Thread[NumRecvWorkers];
        for (int i = 0; i < NumRecvWorkers; i++)
        {
            var S = new NetaSocket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
            S.DualMode = true; // handles v4 and v6
            SetReusePort(S);
            S.ReceiveBufferSize = RecBufferSize;
            S.SendBufferSize = SendBufferSize;
            S.Bind(LocalEndPoint);
            Sockets.Add(S);

            IncomingQueues[i] = new Queue<(NetaSocket Socket, PooledInPacket Packet, EndPointKey EndPointKey)>();
        }
#endif
    }

    void Start_TransportReceiver()
    {
#if WINDOWS
        PostReceives();
#elif LINUX
        ParallelTickHandle = AutoParallelTickManager.RegisterParallelTick(ParallelTick_SocketReceive, 480);
        //for (int i = 0; i < NumRecvWorkers; i++)
        //{
        //    var Socket = Sockets[i];
        //    int RecvWorkerIndex = i;
        //    ReceiveWorkers[i] = new Thread(() => SocketReceiver(Socket, RecvWorkerIndex))
        //    {
        //        Priority = ThreadPriority.AboveNormal,
        //        IsBackground = true,
        //        Name = $"Neta-SRecv-W-{RecvWorkerIndex}"
        //    };
        //
        //    ReceiveWorkers[i].Start();
        //}
#endif
    }


    class NetaServer_Socket_Receive_1;
    class NetaServer_Socket_Receive_2;
    unsafe void SocketReceiver(NetaSocket Socket, int RecvWorkerIndex)
    {
        ParallelTickManager.SetThreadAffinity(RecvWorkerIndex + 2);
    
        const int BatchSize = 64;
        const int MSG_WAITFORONE = 0x10000; // Standard on Linux
    
        Mmsghdr* msgvec = stackalloc Mmsghdr[BatchSize];
        IOVector* iovecs = stackalloc IOVector[BatchSize];
        SockaddrStorage* addresses = stackalloc SockaddrStorage[BatchSize];
        PacketBuffer* PacketBuffers = stackalloc PacketBuffer[BatchSize];

        for (int i = 0; i < BatchSize; i++)
        {
            iovecs[i].Base = (IntPtr)Unsafe.AsPointer(ref PacketBuffers[i][0]);
            iovecs[i].Length = (IntPtr)NetaConsts.BufferMaxSizeBytes;
    
            msgvec[i].msg_hdr.msg_iov = (IntPtr)(&iovecs[i]);
            msgvec[i].msg_hdr.msg_iovlen = (IntPtr)1;
            msgvec[i].msg_hdr.msg_name = (IntPtr)(&addresses[i]);
            msgvec[i].msg_hdr.msg_namelen = sizeof(SockaddrStorage);
        }
    
        var InQueue = IncomingQueues[RecvWorkerIndex];
        int SocketFd = (int)Socket.SafeHandle.DangerousGetHandle();
    
        while (!ShutdownRequested)
        {
            for (int i = 0; i < BatchSize; i++)
            {
                msgvec[i].msg_hdr.msg_namelen = sizeof(SockaddrStorage);
            }
                
            int NumPkts = recvmmsg(SocketFd, msgvec, BatchSize, MSG_WAITFORONE, null);

            if (NumPkts < 1)
            {
                if (NumPkts == -1)
                {
                    int err = Marshal.GetLastPInvokeError();

                    // Ignore the two most common transient cases — do NOT log them
                    if (err == 4 || err == 11)   // EINTR or EAGAIN/EWOULDBLOCK
                        continue;

                    // Only log real problems
                    Logger.LogError($"recvmmsg failed with errno {err} on socket {SocketFd}");
                }
                continue;
            }

            if (NumPkts == 0) continue;
    
            for (int i = 0; i < NumPkts; i++)
            {
                //msgvec[i].msg_hdr.msg_namelen = sizeof(SockaddrStorage);         
                int len = (int)msgvec[i].msg_len;
                if (len <= 0) continue;

                var Key = ExtractKey(ref addresses[i]);

                var InPacket = PooledInPacket.Rent<NetaServer_Socket_Receive_2>();
                ReadOnlySpan<byte> StackSpan = MemoryMarshal.CreateReadOnlySpan(ref PacketBuffers[i][0], len);
                StackSpan.CopyTo(InPacket.GetBuffer());

                InQueue.Enqueue((Socket, InPacket, Key));
            }
        }
    
        while (InQueue.TryDequeue(out var data))
        {
            var (_, Packet, Key) = data;
            Packet.Return();
        }
    }



#if WINDOWS
    public void ParallelTick_Receive(int WorkerIndex)
    {
        var Queue = IncomingQueues[WorkerIndex];

        while (Queue.TryDequeue(out var data))
        {
            var (Socket, Packet, Key) = data;
            try
            {
                ReceivePacket(Socket, Packet, Key, WorkerIndex);
            }
            catch (Exception Ex)
            {
                NetGuard.DebugFail(Ex.ToString());
                Packet.TryReturn();
                if (Cts.IsCancellationRequested || Ex is ObjectDisposedException) break;
                if (Ex is SocketException SocketEx && SocketEx.SocketErrorCode == SocketError.ConnectionReset)
                {
                    if (SocketEx.SocketErrorCode == SocketError.OperationAborted) break;
                    if (SocketEx.SocketErrorCode == SocketError.ConnectionReset)
                    {
                        Logger.Log(ELogLevel.Error, $"{Ex}");
                        continue;
                    }
                }
    
                Logger.Log(ELogLevel.Critical, $"{Ex}"); break;
            }
        }
    }
#elif LINUX
    public void ParallelTick_Receive(int WorkerIndex)
    {
        var Queue = IncomingQueues[WorkerIndex];

        while (Queue.TryDequeue(out var data))
        {
            var (Socket, Packet, Key) = data;
            try
            {
                ReceivePacket(Socket, Packet, Key, WorkerIndex);
            }
            catch (Exception Ex)
            {
                NetGuard.DebugFail(Ex.ToString());
                Packet.TryReturn();
                if (ShutdownRequested || Ex is ObjectDisposedException) break;
                if (Ex is SocketException SocketEx && SocketEx.SocketErrorCode == SocketError.ConnectionReset)
                {
                    if (SocketEx.SocketErrorCode == SocketError.OperationAborted) break;
                    if (SocketEx.SocketErrorCode == SocketError.ConnectionReset)
                    {
                        Logger.Log(ELogLevel.Error, $"{Ex}");
                        continue;
                    }
                }

                Logger.Log(ELogLevel.Critical, $"{Ex}"); break;
            }
        }
    }

    class NetaServer_ParallelTick_Receive;
    public unsafe void ParallelTick_SocketReceive()
    {
        if (ShutdownRequested) return;
        int WorkerIndex = AutoParallelTickManager.WorkerIndex;
        // 1. Setup constants and structures
        const int BatchSize = 1024; // Max amount to pull per syscall
        const int MSG_DONTWAIT = 0x40;

        // Use stackalloc for native headers to avoid GC pressure and pinning
        Mmsghdr* msgvec = stackalloc Mmsghdr[BatchSize];
        IOVector* iovecs = stackalloc IOVector[BatchSize];
        SockaddrStorage* addresses = stackalloc SockaddrStorage[BatchSize]; // Support IPv4/IPv6
        PacketBuffer* PacketBuffers = stackalloc PacketBuffer[BatchSize];

        for (int i = 0; i < BatchSize; i++)
        {
            iovecs[i].Base = (IntPtr)Unsafe.AsPointer(ref PacketBuffers[i][0]);
            iovecs[i].Length = (IntPtr)NetaConsts.BufferMaxSizeBytes;

            msgvec[i].msg_hdr.msg_iov = (IntPtr)(&iovecs[i]);
            msgvec[i].msg_hdr.msg_iovlen = (IntPtr)1;
            msgvec[i].msg_hdr.msg_name = (IntPtr)(&addresses[i]);
            msgvec[i].msg_hdr.msg_namelen = sizeof(SockaddrStorage);
        }

        var Socket = Sockets[WorkerIndex];
        int SocketFd = (int)Socket.SafeHandle.DangerousGetHandle();

        // 3. The Syscall
        // Get the raw FD from your socket: socket.Handle.ToInt32()
        int NumPkts = recvmmsg(SocketFd, msgvec, BatchSize, MSG_DONTWAIT, null);

        if (NumPkts < 0)
        {
            if (NumPkts == -1)
            {
                int err = Marshal.GetLastPInvokeError();

                // Ignore the two most common transient cases
                if (err == 4 || err == 11)   // EINTR or EAGAIN/EWOULDBLOCK
                    return;

                // Only log real problems
                Logger.LogError($"recvmmsg failed with errno {err} on socket {SocketFd}");
            }
            return;
        }

        if (NumPkts == 0) return;

        for (int i = 0; i < NumPkts; i++)
        {
            int len = (int)msgvec[i].msg_len;
            if (len <= 0) continue;

            var InPacket = PooledInPacket.Rent<NetaServer_ParallelTick_Receive>();

            ReadOnlySpan<byte> StackSpan = MemoryMarshal.CreateReadOnlySpan(ref PacketBuffers[i][0], len);
            StackSpan.CopyTo(InPacket.GetBuffer());

            var Key = ExtractKey(ref addresses[i]);

            IncomingQueues[WorkerIndex].Enqueue((Socket, InPacket, Key));
        }
    }
#endif






    void PostReceives()
    {
        foreach (var S in Sockets)
        {
            for (int i = 0; i < ReceiveArgsPerSocket; i++)
            {
                var Args = new SocketAsyncEventArgs();
                Args.RemoteEndPoint = new IPEndPoint(IPAddress.IPv6Any, 0);
                Args.Completed += OnReceiveCompleted;
                SocketArgs.Add(Args);
                PostReceive(S, Args);
            }
        }
    }
    class NetaServer_PostReceive { }
    void PostReceive(NetaSocket Socket, SocketAsyncEventArgs SocketArgs)
    {
        var Packet = PooledInPacket.Rent<NetaServer_PostReceive>();

        try
        {
            SocketArgs.SetBuffer(Packet.GetBuffer());
            SocketArgs.UserToken = (Socket, Packet);
            if (!Socket.ReceiveFromAsync(SocketArgs)) OnReceiveCompleted(null, SocketArgs);
        }
        catch (Exception Ex)
        {
            Packet.Return();

            if (ShutdownRequested || Ex is ObjectDisposedException) { Shutdown(); return; }
            if (Ex is not SocketException SocketEx) throw NetGuard.Fail(Ex.ToString());
            else
            {
                switch (SocketEx.SocketErrorCode)
                {
                    case SocketError.ConnectionReset:
                        Logger.Log(ELogLevel.Warning, Ex.ToString());
                        return;
                    case SocketError.OperationAborted:
                        return;
                    default:
                        throw NetGuard.Fail(Ex.ToString());
                }
            }
        }
    }

    class NetaServer_OnReceiveCompleted { }
    void OnReceiveCompleted(object? sender, SocketAsyncEventArgs SocketArgs)
    {
        int MaxInline = 0;
        while (true)
        {
            var (Socket, Packet) = ((NetaSocket, PooledInPacket))SocketArgs.UserToken!;
            var NewEndPointKey = new EndPointKey((IPEndPoint)SocketArgs.RemoteEndPoint!);
            var WorkerIndex = (int)((uint)NewEndPointKey.GetHashCode() % ParallelTickManager.WorkerCount);
            try
            {
                if (ShutdownRequested || SocketArgs.RemoteEndPoint == null)
                {
                    Packet.TryReturn();
                    return;
                }

                if (SocketArgs.BytesTransferred < 1)
                {
                    TryRemoveEndPoint(ref NewEndPointKey, WorkerIndex);
                    Packet.TryReturn();
                    return;
                }

                IncomingQueues[WorkerIndex].Enqueue((Socket, Packet, NewEndPointKey));
                //IncomingQueue.Enqueue((Socket, Packet, NewEndPointKey));

                Packet = PooledInPacket.Rent<NetaServer_OnReceiveCompleted>();

                SocketArgs.SetBuffer(Packet.GetBuffer());
                SocketArgs.UserToken = (Socket, Packet);

                if (Socket.ReceiveFromAsync(SocketArgs))
                {
                    break;
                }
                else
                {
                    if (MaxInline > 128)
                    {
                        ThreadPool.QueueUserWorkItem(_ => { OnReceiveCompleted(null, SocketArgs); });
                        return;
                    }

                    MaxInline++;
                    continue;
                }
            }
            catch (Exception Ex)
            {
                if (SocketArgs.RemoteEndPoint != null)
                {
                    TryRemoveEndPoint(NewEndPointKey, WorkerIndex, Ex);
                }

                if (Ex is AlreadyInPoolException)
                {
                    Packet.TryReturn();
                    Logger.LogError($"OnReceiveCompleted exception: {Ex}");
                }
                else if (Ex is SocketException SocketEx)
                {
                    Packet.TryReturn();
                    switch (SocketEx.SocketErrorCode)
                    {
                        case SocketError.ConnectionReset:
                            Logger.LogWarning(Ex.ToString());
                            return;
                        case SocketError.OperationAborted:
                            return;
                        default:
                            Logger.LogCritical($"OnReceiveCompleted unhandled SocketException: {Ex}");
                            Shutdown(); return;
                    }
                }
                else if (Ex is ObjectDisposedException) { Packet.Return(); Shutdown(); return; }
                else if (Ex is InvalidOperationException)
                {
                    Packet.TryReturn();
                    Logger.LogCritical($"OnReceiveCompleted exception: {Ex}");
                }
                else
                {
                    Packet.TryReturn();
                    Logger.LogCritical($"OnReceiveCompleted unhandled exception: {Ex}");
                    Shutdown(); return;
                }
            }
        }
    }
}