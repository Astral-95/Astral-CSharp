#if LINUX
using Astral.Network.Sockets;
using Astral.Network.Toolkit;
using Astral.Network.Transport;
using Astral.Tick;
using System.Buffers.Binary;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Astral.Network.Servers;

public partial class NetaServer
{

    

    

    ParallelTickHandle LinuxSendParallelTickHandle;

    [ThreadStatic]
    static int OutgoingQueueCount = 0;
    PendingOutPacket[][] WorkerOutgoingQueue = new PendingOutPacket[ParallelTickManager.WorkerCount][];

    void Initialize_TransmitterLinux()
    {
        for (int i = 0; i < WorkerOutgoingQueue.Length; i++)
        {
            var NewList = new List<PendingOutPacket>(4096);
            for (int j = 0; j < 4096; j++) NewList.Add(default);
            WorkerOutgoingQueue[i] = new PendingOutPacket[4096];
        }
    }

    void Start_TransportLinuxTransmitter()
    {
        //LinuxSendParallelTickHandle = ParallelTickManager.RegisterParallelTick(ParallelTick_Send, 480);
    }

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int sendmmsg(int sockfd, Mmsghdr* msgvec, uint vlen, int flags);

    

    

    // Pre-allocated per-worker scratch arrays — resize as needed, never shrink
    [ThreadStatic] private static nint _msgvecPtr;
    [ThreadStatic] private static nint _iovecsPtr;
    [ThreadStatic] private static int _scratchCapacity;


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
        //if (ShutdownRequested)
        //{
        //    if (LinuxSendParallelTickHandle.IsValid())
        //    {
        //        ParallelTickManager.UnregisterParallelTick(LinuxSendParallelTickHandle);
        //        LinuxSendParallelTickHandle = default;
        //    }
        //    return;
        //}
        //int WorkerIndex = ParallelTickManager.WorkerIndex;

        var Queue = WorkerOutgoingQueue[WorkerIndex];

        //var TargetIndex = NumRecvWorkers == 1 ? 0 : WorkerIndex % NumRecvWorkers;
        //var Socket = Sockets[TargetIndex];
        var Socket = Sockets[WorkerIndex];
        
        if (OutgoingQueueCount == 0) return;

        // Grow scratch arrays if needed
        EnsureScratch(OutgoingQueueCount);

        try
        {
            Mmsghdr* pMsgVec = (Mmsghdr*)_msgvecPtr;
            IOVector* pIoVecs = (IOVector*)_iovecsPtr;

            fixed (PendingOutPacket* pQueue = Queue)
            {
                for (int i = 0; i < OutgoingQueueCount; i++)
                {
                    byte* pPacketData = (byte*)&pQueue[i].Data;
                    void* pPacketAddr = &pQueue[i].Destination;

                    pIoVecs[i].Base = (IntPtr)pPacketData;
                    pIoVecs[i].Length = (IntPtr)pQueue[i].Length;

                    pMsgVec[i].msg_hdr.msg_name = (IntPtr)pPacketAddr;
                    pMsgVec[i].msg_hdr.msg_namelen = Queue[i].Destination.Len;
                    pMsgVec[i].msg_hdr.msg_iov = (IntPtr)(&pIoVecs[i]);
                    pMsgVec[i].msg_hdr.msg_iovlen = (IntPtr)1;
                    pMsgVec[i].msg_hdr.msg_control = IntPtr.Zero;
                    pMsgVec[i].msg_hdr.msg_controllen = IntPtr.Zero;
                    pMsgVec[i].msg_hdr.msg_flags = 0;
                    pMsgVec[i].msg_len = 0;
                }

                int fd = (int)Socket.SafeHandle.DangerousGetHandle();
                int sent = sendmmsg(fd, pMsgVec, (uint)OutgoingQueueCount, 0);

                if (sent < 0)
                {
                    int err = Marshal.GetLastWin32Error();
                    Logger.LogError($"sendmmsg failed: errno {err}");
                }
            }
        }
        finally
        {
            OutgoingQueueCount = 0;
        }
    }

    internal void EnqueueOutPacket(OutPacket Packet, SocketAddrStorage Addr, int WorkerIndex)
    {
        PendingOutPacket[] Queue = WorkerOutgoingQueue[WorkerIndex];

        if (OutgoingQueueCount >= Queue.Length)
        {
            int NewSize = Queue.Length == 0 ? 64 : Queue.Length * 2;
            Array.Resize(ref Queue, NewSize);
            WorkerOutgoingQueue[WorkerIndex] = Queue;
        }

        ref PendingOutPacket Pending = ref Queue[OutgoingQueueCount++];

        //Packet.GetBuffer()[..Packet.Pos].CopyTo(MemoryMarshal.CreateSpan(ref Pending.Data[0], Packet.Pos));
        Packet.GetBuffer().AsSpan(0, Packet.Pos).CopyTo(MemoryMarshal.CreateSpan(ref Pending.Data[0], Packet.Pos));
        Pending.Length = Packet.Pos;
        Pending.Destination = Addr;
    }

    void Cleanup_TransportLinuxTransmitter(int WorkerIndex)
    {
        
    }
}
#endif