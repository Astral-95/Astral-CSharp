using Astral.Logging;
using Astral.Network.Connections;
using Astral.Network.Drivers;
using Astral.Network.Toolkit;
using Astral.Threading;
using Astral.Tick;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;

namespace Astral.Network.Tools;

public class ClientConnections
{
    private class WorkerChunk
    {
        public readonly List<NetaConnection> Clients;
        public ReadWriteSpinLock Lock;

        public WorkerChunk(int Capacity)
        {
            Clients = new List<NetaConnection>(Capacity);
            Lock = new ReadWriteSpinLock();
        }
    }

    private readonly int NumWorkers;
    private readonly WorkerChunk[] Chunks;
    public ConcurrentDictionary<NetaAddress, NetaConnection> EndPointConnectionMap { get; internal set; } = new();

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    [ThreadStatic]
    static Queue<(NetaAddress Endpoint, DateTime Expiry)> RecentlyClosedEndPointsQueue;

    [ThreadStatic]
    static Dictionary<NetaAddress, byte> RecentlyClosedEndPointsMap;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

    public ConcurrentDictionary<NetaAddress, bool> BlockedEndPoints { get; internal set; } = new();

    List<NetaConnection>[] WorkerConnectionRemoveQueue = new List<NetaConnection>[ParallelTickManager.WorkerCount];

    ReadWriteSpinLock RecentlyClosedEndPointsQueueLock = new ReadWriteSpinLock();



    int INumConnected = 0;
    public int NumConnected { get => Volatile.Read(ref INumConnected); }



    public ClientConnections(int NumWorkers, int InitialCapacityPerWorker)
    {
        this.NumWorkers = NumWorkers;
        Chunks = new WorkerChunk[NumWorkers];
        for (int i = 0; i < NumWorkers; i++)
        {
            Chunks[i] = new WorkerChunk(InitialCapacityPerWorker);
            WorkerConnectionRemoveQueue[i] = new();
        }
    }

    // private WorkerChunk GetChunk(int Index) => LocalChunk ??= Chunks[Index];
    private WorkerChunk GetChunk(int Index) => Chunks[ParallelTickManager.WorkerIndex]!;
    internal List<NetaConnection> GetLocalList()
    {
        return Chunks[ParallelTickManager.WorkerIndex]!.Clients;
    }
    public bool IsEndPointConnectionAllowed(NetaAddress Address)
    {
        if (RecentlyClosedEndPointsMap == null)
        {
            RecentlyClosedEndPointsMap = new();
            RecentlyClosedEndPointsQueue = new();
        }
        return !(BlockedEndPoints.ContainsKey(Address) || RecentlyClosedEndPointsMap.ContainsKey(Address));
    }


    internal void Add(NetaConnection Client, ref NetaAddress Address)
    {
        Interlocked.Increment(ref INumConnected);
        var WorkerIndex = ParallelTickManager.WorkerIndex;
        EndPointConnectionMap.TryAdd(Address, Client);

        var Chunk = GetChunk(WorkerIndex);
        Chunk.Lock.EnterWrite();
        Chunk.Clients.Add(Client);
        Chunk.Lock.ExitWrite();
    }

    internal void Remove(NetaConnection Client)
    {
        Interlocked.Decrement(ref INumConnected);
        var WorkerIndex = ParallelTickManager.WorkerIndex;

        var Chunk = GetChunk(WorkerIndex);

        Chunk.Lock.EnterWrite();
        Chunk.Clients.Remove(Client);
        Chunk.Lock.ExitWrite();

        OnEndPointRemoved(ref Client.NetaRemoteAddr, WorkerIndex);
    }

    internal void Remove(ref NetaAddress Address, bool Block = false)
    {
        var WorkerIndex = ParallelTickManager.WorkerIndex;

        if (EndPointConnectionMap.TryRemove(Address, out var Connection))
        {
            Interlocked.Decrement(ref INumConnected);

            var Chunk = GetChunk(WorkerIndex);

            Chunk.Lock.EnterWrite();
            Chunk.Clients.Remove(Connection);
            Chunk.Lock.ExitWrite();

            if (Block)
            {
                BlockEndPoint(ref Address);
            }
            else
            {
                OnEndPointRemoved(ref Address, WorkerIndex);
            }
        }
    }

    internal void EnqueueRemove(NetaConnection Client, bool Block = false)
    {
        int WorkerIndex = Client.WorkerIndex;
        WorkerConnectionRemoveQueue[WorkerIndex].Add(Client);

        if (!EndPointConnectionMap.Remove(Client.NetaRemoteAddr, out var _))
        {
            AstralLoggingCenter.Log("ClientConnects", ELogLevel.Error, "Removing a client with an address that was not registered.");
            return;
        }

        EndPointConnectionMap.TryRemove(Client.NetaRemoteAddr, out var _);

        if (Block)
        {
            BlockEndPoint(ref Client.NetaRemoteAddr);
        }
        else
        {
            OnEndPointRemoved(ref Client.NetaRemoteAddr, WorkerIndex);
        }
    }

    internal void EnqueueRemove(ref NetaAddress Address, int WorkerIndex, bool Block = false)
    {
        if (Block)
        {
            BlockEndPoint(ref Address);
        }
        else
        {
            OnEndPointRemoved(ref Address, WorkerIndex);
        }

        if (EndPointConnectionMap.TryRemove(Address, out var Connection))
        {
            WorkerConnectionRemoveQueue[WorkerIndex].Add(Connection);
        }
    }



    internal void BlockEndPoint(ref NetaAddress Address) => BlockedEndPoints.TryAdd(Address, true);


    public bool TryGetConnection(ref NetaAddress Address, [MaybeNullWhen(false)] out NetaConnection Connection) => EndPointConnectionMap.TryGetValue(Address, out Connection);

    internal void Tick_RemoveQueue(int WorkerIndex)
    {
        var Queue = WorkerConnectionRemoveQueue[WorkerIndex];

        if (Queue.Count > 0)
        {
            foreach (var Connection in Queue)
            {
                Interlocked.Decrement(ref INumConnected);

                var Chunk = GetChunk(WorkerIndex);

                Chunk.Lock.EnterWrite();
                Chunk.Clients.Remove(Connection);
                Chunk.Lock.ExitWrite();
            }

            Queue.Clear();
        }
    }

    void OnEndPointRemoved(ref NetaAddress EndPointKey, int WorkerIndex)
    {
        if (RecentlyClosedEndPointsMap == null)
        {
            RecentlyClosedEndPointsMap = new();
            RecentlyClosedEndPointsQueue = new();
        }

        RecentlyClosedEndPointsMap.TryAdd(EndPointKey, 0);
        RecentlyClosedEndPointsQueue.Enqueue((EndPointKey, DateTime.UtcNow.AddSeconds(2)));

        if (RecentlyClosedEndPointsMap.Count == 1)
        {
            EndpointExpirationCleanupTickId = ParallelTickManager.Register(EndpointExpirationCleanupTick, 5, WorkerIndex: WorkerIndex);
        }
    }

    TickHandle EndpointExpirationCleanupTickId = default;
    void EndpointExpirationCleanupTick()
    {
        RecentlyClosedEndPointsQueueLock.EnterWrite();
        var Now = DateTime.UtcNow;

        while (RecentlyClosedEndPointsQueue.Count > 0 && RecentlyClosedEndPointsQueue.Peek().Expiry <= Now)
        {
            var Expired = RecentlyClosedEndPointsQueue.Dequeue();
            RecentlyClosedEndPointsMap.Remove(Expired.Endpoint);
        }

        if (RecentlyClosedEndPointsQueue.Count == 0)
        {
            ParallelTickManager.Unregister(ref EndpointExpirationCleanupTickId);
        }
        RecentlyClosedEndPointsQueueLock.ExitWrite();
    }

    internal void Shutdown()
    {
        var MapSnapshot = EndPointConnectionMap.ToArray();
        foreach (var Pair in MapSnapshot)
        {
            Pair.Value.Shutdown();
        }

        for (int i = 0; i < Chunks.Length; i++)
        {
            Chunks[i].Clients.Clear();
            //Chunks[i] = null!;
        }

        if (EndpointExpirationCleanupTickId.IsValid())
        {
            ParallelTickManager.Unregister(ref EndpointExpirationCleanupTickId);
        }
    }

    internal async Task WaitForCompletionAsync()
    {
        foreach (var Kvp in EndPointConnectionMap)
        {
            await Kvp.Value.WaitForCompletionAsync();
        }

        for (int i = 0; i < Chunks.Length; i++)
        {
            Chunks[i] = null!;
        }

        EndPointConnectionMap.Clear();
    }


    public Enumerator GetEnumerator() => new Enumerator(this);

    public struct Enumerator : IDisposable
    {
        private readonly ClientConnections _parent;
        private int _chunkIndex;
        private int _clientIndex;
        private WorkerChunk? _currentChunk;

        internal Enumerator(ClientConnections parent)
        {
            _parent = parent;
            _chunkIndex = 0;
            _clientIndex = -1;
            _currentChunk = null;
        }

        public NetaConnection Current => _currentChunk!.Clients[_clientIndex];

        public bool MoveNext()
        {
            // 1. Move to the next slot in the current chunk
            _clientIndex++;

            // 2. If we don't have a chunk yet, or we've exhausted the current one
            while (_currentChunk == null || _clientIndex >= _currentChunk.Clients.Count)
            {
                // Release current lock before moving on
                _currentChunk?.Lock.ExitRead();
                _currentChunk = null;

                // Are there more chunks to check?
                if (_chunkIndex >= _parent.NumWorkers)
                {
                    return false; // Totally done
                }

                // Move to the next chunk
                _currentChunk = _parent.Chunks[_chunkIndex++];
                _currentChunk.Lock.EnterRead();

                // Reset index to 0 for the new chunk
                _clientIndex = 0;

                // If this chunk actually has data, we found our next element
                if (_clientIndex < _currentChunk.Clients.Count)
                {
                    return true;
                }

                // Chunk was empty, loop again to find a non-empty one
            }

            return true;
        }

        public void Dispose() => _currentChunk?.Lock.ExitRead();
    }
}