using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Astral.Containers;

public class ObjectStack<T> where T : class
{
    private T?[] Items;
    private int Index = -1;
    public int Count => Index + 1;

    public ObjectStack(int InitialCapacity = 1024)
    {
        Items = new T[InitialCapacity];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T Item)
    {
        if (Index + 1 >= Items.Length)
            Array.Resize(ref Items, Items.Length * 2);
        Items[++Index] = Item;
    }

    public T? Take()
    {
        if (Index < 0) { return null; }
        var Item = Items[Index]!;
        Items[Index--] = null;
        return Item;
    }

    public bool Take([NotNullWhen(true)] out T? Item)
    {
        if (Index < 0) { Item = null; return false; }
        Item = Items[Index]!;
        Items[Index--] = null;
        return true;
    }


    public T? Peak()
    {
        if (Index < 0) { return null; }
        var Item = Items[Index]!;
        Items[Index - 1] = null;
        return Item;
    }

    public bool Peak([NotNullWhen(true)] out T? Item)
    {
        if (Index < 0) { Item = null; return false; }
        Item = Items[Index]!;
        Items[Index - 1] = null;
        return true;
    }
}