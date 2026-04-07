using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Astral.Containers;

public class ObjectStack<T> where T : class
{
    int ChunkSize = 1024;
    sealed class ObjectChunk
    {
        public readonly T[] Items;
        public int Index = -1;
        public ObjectChunk? PreviousChunk;
        public ObjectChunk(int Size = 1024)
        {
            Items = new T[Size];
        }
    }

    public int Count { get; private set; }


    private ObjectChunk CurrentChunk = new ObjectChunk();

    public ObjectStack(int ChunkSize = 1024)
    {
        this.ChunkSize = ChunkSize;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T Item)
    {
        // If current chunk is full, link a new one
        if (CurrentChunk.Index >= ChunkSize)
        {
            ObjectChunk NextChunk = new ObjectChunk(ChunkSize);
            NextChunk.PreviousChunk = CurrentChunk;
            CurrentChunk = NextChunk;
        }

        CurrentChunk.Items[++CurrentChunk.Index] = Item;
        Count++;
    }

    public T? Take()
    {
        // If the current chunk is empty, try to move to the previous one
        if (CurrentChunk.Index < 0)
        {
            if (CurrentChunk.PreviousChunk == null)
            {
                return null;
            }

            CurrentChunk = CurrentChunk.PreviousChunk;
        }

        Count--;
        return CurrentChunk.Items[CurrentChunk.Index--];
    }

    public bool Take([NotNullWhen(true)] out T? Item)
    {
        // If the current chunk is empty, try to move to the previous one
        if (CurrentChunk.Index < 0)
        {
            if (CurrentChunk.PreviousChunk == null)
            {
                Item = null;
                return false;
            }

            CurrentChunk = CurrentChunk.PreviousChunk;
        }
        Count--;
        Item = CurrentChunk.Items[CurrentChunk.Index--];
        return true;
    }


    public T? Peak()
    {
        ObjectChunk CurrChunk = CurrentChunk;
        while (true)
        {
            if (CurrChunk.Index > -1)
            {
                return CurrChunk.Items[CurrChunk.Index];
            }

            if (CurrChunk.PreviousChunk == null)
            {
                return null;
            }

            CurrChunk = CurrChunk.PreviousChunk;
        }
    }

    public bool Peak([NotNullWhen(true)] out T? Item)
    {
        ObjectChunk CurrChunk = CurrentChunk;
        while (true)
        {
            if (CurrChunk.Index > -1)
            {
                Item = CurrChunk.Items[CurrChunk.Index];
                return true;
            }

            if (CurrChunk.PreviousChunk == null)
            {
                Item = null;
                return false;
            }

            CurrChunk = CurrChunk.PreviousChunk;
        }    
    }
}