#if LINUX
using Astral.Logging;
using Astral.Network.Sockets;
using Astral.Network.Toolkit;
using Astral.Network.Tools;
using Astral.Network.Transport;
using Astral.Tick;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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

    unsafe class LinuxRecieveWorkerState
    {
        public readonly uint BatchSize;

        // Managed pinned arrays
        private readonly Mmsghdr[] MsgVec;
        private readonly IOVector[] IoVecs;
        private readonly SocketAddrStorage[] Addresses;
        public readonly PooledInPacket[] Packets;

        // Direct pointers for high-performance access
        public readonly Mmsghdr* MsgVecPtr;
        public readonly IOVector* IoVecsPtr;
        public readonly SocketAddrStorage* AddressesPtr;

        public LinuxRecieveWorkerState(int BatchSize)
        {
            this.BatchSize = (uint)BatchSize;

            // Allocate pinned arrays to ensure pointers remain valid for the life of the object
            MsgVec = GC.AllocateArray<Mmsghdr>(BatchSize, pinned: true);
            IoVecs = GC.AllocateArray<IOVector>(BatchSize, pinned: true);
            Addresses = GC.AllocateArray<SocketAddrStorage>(BatchSize, pinned: true);
            Packets = GC.AllocateArray<PooledInPacket>(BatchSize, pinned: true);

            // Cache the pointers so we don't have to fix them every tick
            MsgVecPtr = (Mmsghdr*)Unsafe.AsPointer(ref MsgVec[0]);
            IoVecsPtr = (IOVector*)Unsafe.AsPointer(ref IoVecs[0]);
            AddressesPtr = (SocketAddrStorage*)Unsafe.AsPointer(ref Addresses[0]);

            for (int i = 0; i < BatchSize; i++)
            {
                var Pkt = PooledInPacket.Rent<LinuxRecieveWorkerState>();
                Packets[i] = Pkt;
                // Point the IOVector to the specific PacketBuffer index
                IoVecsPtr[i].Base = (IntPtr)Pkt.GetBuffer();
                IoVecsPtr[i].Length = (IntPtr)NetaConsts.BufferMaxSizeBytes;

                // Link the Mmsghdr to the corresponding IOVector and Address slot
                MsgVecPtr[i].msg_hdr.msg_iov = (IntPtr)(&IoVecsPtr[i]);
                MsgVecPtr[i].msg_hdr.msg_iovlen = (IntPtr)1;
                MsgVecPtr[i].msg_hdr.msg_name = (IntPtr)(&AddressesPtr[i]);
                MsgVecPtr[i].msg_hdr.msg_namelen = sizeof(SocketAddrStorage);
            }
        }
    }

    Thread[] ReceiveWorkers;

    [ThreadStatic]
    static LinuxRecieveWorkerState WorkerState = null!;

    //ParallelTickHandle LinuxRecvParallelTickHandle;
    //TickHandle[] LinuxRecvWorkerTickHandles;

    //Queue<(NetaSocket Socket, PooledInPacket Packet, NetaAddress EndPointKey)>[] WorkerRecvQueues;


    void Initialize_TransportLinuxReceiver(IPEndPoint LocalEndPoint, int RecBufferSize = 2 * 1024 * 1024, int SendBufferSize = 2 * 1024 * 1024)
    {
        //WorkerRecvQueues = new Queue<(NetaSocket Socket, PooledInPacket Packet, NetaAddress EndPointKey)>[ParallelTickManager.WorkerCount];

        ReceiveWorkers = new Thread[ParallelTickManager.WorkerCount];
        //LinuxRecvWorkerTickHandles = new TickHandle[ParallelTickManager.WorkerCount];
        for (int i = 0; i < ParallelTickManager.WorkerCount; i++)
        {
            var S = new NetaSocket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
            S.DualMode = true; // handles v4 and v6
            SetReusePort(S);
            S.ReceiveBufferSize = RecBufferSize;
            S.SendBufferSize = SendBufferSize;
            S.Bind(LocalEndPoint);
            Sockets.Add(S);

            //WorkerRecvQueues[i] = new Queue<(NetaSocket Socket, PooledInPacket Packet, NetaAddress EndPointKey)>();
            //LinuxRecvWorkerTickHandles[i] = default;
        }
    }

    void Start_TransportLinuxReceiver()
    {
        //for (int i = 0; i < ParallelTickManager.WorkerCount; i++)
        //{
        //    LinuxRecvWorkerTickHandles[i] = ParallelTickManager.Register(Tick_SocketReceive, 480, WorkerIndex: i);
        //}
    }

    //public void ParallelTick_Receive(int WorkerIndex)
    //{
    //    var Queue = WorkerRecvQueues[WorkerIndex];
    //
    //    while (Queue.TryDequeue(out var data))
    //    {
    //        var (Socket, Packet, Key) = data;
    //        try
    //        {
    //            ReceivePacket(Socket, Packet, Key, WorkerIndex);
    //        }
    //        catch (Exception Ex)
    //        {
    //            NetGuard.DebugFail(Ex.ToString());
    //            Packet.TryReturn();
    //            if (ShutdownRequested || Ex is ObjectDisposedException) break;
    //            if (Ex is SocketException SocketEx && SocketEx.SocketErrorCode == SocketError.ConnectionReset)
    //            {
    //                if (SocketEx.SocketErrorCode == SocketError.OperationAborted) break;
    //                if (SocketEx.SocketErrorCode == SocketError.ConnectionReset)
    //                {
    //                    Logger.Log(ELogLevel.Error, $"{Ex}");
    //                    continue;
    //                }
    //            }
    //
    //            Logger.Log(ELogLevel.Critical, $"{Ex}"); break;
    //        }
    //    }
    //}


    class NetaServer_Tick_SocketReceive;
    unsafe void ParallelTick_Receive(int WorkerIndex)
    {
        if (WorkerState == null)
        {
            WorkerState = new LinuxRecieveWorkerState(1024);
        }

        while (true)
        {
            const int MSG_DONTWAIT = 0x40;

            var Socket = Sockets[WorkerIndex];
            int SocketFd = (int)Socket.SafeHandle.DangerousGetHandle();

            int NumPkts = recvmmsg(SocketFd, WorkerState.MsgVecPtr, WorkerState.BatchSize, MSG_DONTWAIT, null);

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
                int len = (int)WorkerState.MsgVecPtr[i].msg_len;
                WorkerState.MsgVecPtr[i].msg_hdr.msg_namelen = sizeof(SocketAddrStorage);

                var InPacket = WorkerState.Packets[i];
                var Key = new NetaAddress(ref WorkerState.AddressesPtr[i]);

                try
                {
                    ReceivePacket(Socket, InPacket, Key, WorkerIndex);
                }
                catch (Exception Ex)
                {
                    NetGuard.DebugFail(Ex.ToString());
                }

                var NewInPacket = PooledInPacket.Rent<NetaServer_Tick_SocketReceive>();
                WorkerState.Packets[i] = NewInPacket;
                WorkerState.IoVecsPtr[i].Base = (IntPtr)NewInPacket.GetBuffer();
            }

            if (NumPkts < WorkerState.BatchSize) return;
        }
    }

    void Cleanup_TransportLinuxReceive(int WorkerIndex)
    {
        if (WorkerState != null)
        {
            Sockets[WorkerIndex].Dispose();

            foreach (var Packet in WorkerState.Packets)
            {
                Packet.Return<NetaServer_Tick_SocketReceive>();
            }
            WorkerState = null!;
        }
    }
}

#endif