using System.Collections;

namespace Astral.Containers;

public class ConcurrentChunkedArray<T> : IEnumerable<T>, IEnumerable
{
    private readonly int ChunkSize;
    private readonly List<Chunk> Chunks = new();

    public ConcurrentChunkedArray(int ChunkSize = 4096)
    {
        if (ChunkSize <= 0) throw new ArgumentException("Chunk size must be > 0");
        this.ChunkSize = ChunkSize;
    }

    private class Chunk
    {
        public List<T> Items;
        public Chunk(int chunkSize)
        {
            Items = new List<T>(chunkSize);
        }
    }

    // Add item sequentially (auto-append)
    public void Add(T Item)
    {
        Chunk Chunk;

        lock (Chunks)
        {
            if (Chunks.Count == 0 || Chunks[^1].Items.Count >= ChunkSize)
            {
                var newChunk = new Chunk(ChunkSize);
                Chunks.Add(newChunk);
            }
            Chunk = Chunks[^1]; // get last chunk
        }

        lock (Chunk)
        {
            Chunk.Items.Add(Item);
        }
    }


    public bool TrySet(int Index, T Value)
    {
        if (Index < 0) return false;

        lock (Chunks)
        {
            int ChunkIndex = Index / ChunkSize;
            if (ChunkIndex < 0 || ChunkIndex >= Chunks.Count) return false;

            Chunk Chunk = Chunks[ChunkIndex];

            lock (Chunk)
            {
                int InnerIndex = Index - ChunkIndex * ChunkSize;
                if (InnerIndex < 0 || InnerIndex >= Chunk.Items.Count) return false;

                Chunk.Items[InnerIndex] = Value;
                return true;
            }
        }
    }

    public bool TryGet(int Index, out T Value)
    {
        Value = default!;
        if (Index < 0) return false;

        lock (Chunks)
        {
            int ChunkIndex = Index / ChunkSize;
            if (ChunkIndex < 0 || ChunkIndex >= Chunks.Count) return false;

            Chunk Chunk = Chunks[ChunkIndex];

            lock (Chunk)
            {
                int InnerIndex = Index - ChunkIndex * ChunkSize;
                if (InnerIndex < 0 || InnerIndex >= Chunk.Items.Count) return false;

                Value = Chunk.Items[InnerIndex];
                return true;
            }
        }
    }

    public T this[int Index]
    {
        get
        {
            if (Index < 0) throw new IndexOutOfRangeException();

            lock (Chunks)
            {
                int ChunkIndex = Index / ChunkSize;

                if (ChunkIndex < 0 || ChunkIndex >= Chunks.Count) throw new IndexOutOfRangeException();

                Chunk Chunk = Chunks[ChunkIndex];

                lock (Chunk)
                {
                    int InnerIndex = Index - ChunkIndex * ChunkSize;
                    if (InnerIndex >= Chunk.Items.Count) throw new IndexOutOfRangeException();
                    return Chunk.Items[InnerIndex];
                }
            }
        }

        set
        {
            if (Index < 0) throw new IndexOutOfRangeException();

            lock (Chunks)
            {
                int ChunkIndex = Index / ChunkSize;
                if (ChunkIndex < 0 || ChunkIndex >= Chunks.Count) throw new IndexOutOfRangeException();

                Chunk Chunk = Chunks[ChunkIndex];

                lock (Chunk)
                {
                    int InnerIndex = Index - ChunkIndex * ChunkSize;
                    if (InnerIndex >= Chunk.Items.Count) throw new IndexOutOfRangeException();

                    Chunk.Items[InnerIndex] = value;
                }
            }
        }
    }

    public int Count
    {
        get
        {
            int count = 0;
            lock (Chunks)
            {
                if (Chunks.Count == 0) return 0;
                for (int i = 0; i < Chunks.Count - 1; i++)
                    count += Chunks[i].Items.Count;
                count += Chunks[^1].Items.Count;
            }
            return count;
        }
    }

    // Optional: iterate over all items safely
    public void ForEach(Action<T> action)
    {
        int ChunkCount;

        lock (Chunks) ChunkCount = Chunks.Count;

        for (int i = 0; i < ChunkCount; i++)
        {
            Chunk Chunk;

            lock (Chunks)
            {
                if (i >= Chunks.Count)
                    break;
                Chunk = Chunks[i];
            }

            lock (Chunk)
            {
                for (int j = 0; j < Chunk.Items.Count; j++)
                {
                    action(Chunk.Items[j]);
                }
            }
        }
    }



    public IEnumerator<T> GetEnumerator()
    {
        int ChunkCount;

        lock (Chunks) // lock main array briefly to get total chunk count
        {
            ChunkCount = Chunks.Count;
        }

        for (int i = 0; i < ChunkCount; i++)
        {
            Chunk Chunk;

            // Lock main array briefly to get the chunk reference
            lock (Chunks)
            {
                if (i >= Chunks.Count)
                    yield break; // in case a new chunk is added concurrently
                Chunk = Chunks[i];
            }

            // Lock the chunk and iterate its items
            lock (Chunk)
            {
                for (int j = 0; j < Chunk.Items.Count; j++)
                {
                    yield return Chunk.Items[j];
                }
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}