using Astral.Logging;
using Astral.Network.Sockets;
using Astral.Network.Toolkit;
using Astral.Network.Tools;
using Astral.Network.Transport;
using Astral.Tick;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Astral.Exceptions;
using System.Collections.Concurrent;

namespace Astral.Network.Servers;

public partial class NetaServer
{

    private readonly SocketAddress[] WorkerRecvAddresses = new SocketAddress[ParallelTickManager.WorkerCount];

#if !LINUX
    int ReceiveArgsPerSocket = 128;
    List<SocketAsyncEventArgs> SocketArgs = new List<SocketAsyncEventArgs>(128);

    private readonly ConcurrentQueue<(PooledInPacket Packet, NetaAddress NetaAddress)>[] WorkerRecvQueues = new ConcurrentQueue<(PooledInPacket Packet, NetaAddress NetaAddress)>[ParallelTickManager.WorkerCount];
#endif
    



    void Initialize_Receiver(IPEndPoint LocalEndPoint, int RecBufferSize = 2 * 1024 * 1024, int SendBufferSize = 2 * 1024 * 1024)
    {
        for (int i = 0; i < ParallelTickManager.WorkerCount; i++)
        {
            // 16 bytes for IPv4, 28 bytes for IPv6. Use 28 to be safe.
            WorkerRecvAddresses[i] = new SocketAddress(AddressFamily.InterNetworkV6, 28);
        }

#if !LINUX
        Socket = new NetaSocket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
        Socket.DualMode = true; // handles v4 and v6
        Socket.Blocking = false;
        SetReusePort(Socket);
        Socket.ReceiveBufferSize = RecBufferSize;
        Socket.SendBufferSize = SendBufferSize;
        Socket.Bind(LocalEndPoint);

        for (int i = 0; i < ParallelTickManager.WorkerCount; i++)
        {
            WorkerRecvQueues[i] = new ConcurrentQueue<(PooledInPacket Packet, NetaAddress NetaAddress)>();
        }
#else
        Initialize_TransportLinuxReceiver(LocalEndPoint, RecBufferSize, SendBufferSize);
#endif
    }


#if !LINUX
    class NetaServer_ParallelTick_Receive;
    public void ParallelTick_Receive(int WorkerIndex)
    {
        var Queue = WorkerRecvQueues[WorkerIndex];

        while (Queue.TryDequeue(out var data))
        {
            var (Packet, Key) = data;
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






    void PostReceives()
    {
        for (int i = 0; i < ReceiveArgsPerSocket; i++)
        {
            var Args = new SocketAsyncEventArgs();
            Args.RemoteEndPoint = new IPEndPoint(IPAddress.IPv6Any, 0);
            Args.Completed += OnReceiveCompleted;
            SocketArgs.Add(Args);
            PostReceive(Socket, Args);
        }
    }
    class NetaServer_PostReceive { }
    void PostReceive(NetaSocket Socket, SocketAsyncEventArgs SocketArgs)
    {
        var Packet = PooledInPacket.Rent<NetaServer_PostReceive>();

        try
        {
            SocketArgs.SetBuffer(Packet.Memory);
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
            var NetaAddress = new NetaAddress((IPEndPoint)SocketArgs.RemoteEndPoint!);
            var WorkerIndex = ParallelTickManager.GetWorkerIndexForHash(NetaAddress.Hash);
            try
            {
                if (ShutdownRequested || SocketArgs.RemoteEndPoint == null)
                {
                    Packet.Return();
                    return;
                }

                if (SocketArgs.BytesTransferred < 1)
                {
                    ClientConnections.EnqueueRemove(ref NetaAddress, WorkerIndex);
                    Packet.Return();
                    return;
                }

                WorkerRecvQueues[WorkerIndex].Enqueue((Packet, NetaAddress));

                Packet = PooledInPacket.Rent<NetaServer_OnReceiveCompleted>();

                SocketArgs.SetBuffer(Packet.Memory);
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
                    ClientConnections.EnqueueRemove(ref NetaAddress, WorkerIndex);
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


#endif

    void Cleanup_TransportReceiver(int WorkerIndex)
    {
#if LINUX
        Cleanup_TransportLinuxReceive(WorkerIndex);
#endif
    }
}