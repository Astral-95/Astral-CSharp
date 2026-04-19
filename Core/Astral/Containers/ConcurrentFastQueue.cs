using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Astral.Containers;

public class ConcurrentFastQueue<T> where T : class
{
    // Padding before: 64 bytes to isolate from the class header/metadata
    private long p1, p2, p3, p4, p5, p6, p7, p8;

    private readonly ConcurrentQueue<T> Items = new();

    // Padding between: 64 bytes to isolate the Queue from the FastItem
    private long p9, p10, p11, p12, p13, p14, p15, p16;

    private T? FastItem;

    // Padding after: 64 bytes to isolate FastItem from anything else
    private long p17, p18, p19, p20, p21, p22, p23, p24;

    /// <summary>
    /// +- 1
    /// </summary>
    public int Count => Items.Count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? TryDequeue()
    {
        var Item = FastItem;
        if (Item == null || Interlocked.CompareExchange(ref FastItem, null, Item) != Item)
        {
            if (Items.TryDequeue(out Item))
            {
                return Item;
            }
            return null;
        }

        return Item;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryDequeue([MaybeNullWhen(false)] out T OutItem)
    {
        OutItem = FastItem;

        if (OutItem != null && Interlocked.CompareExchange(ref FastItem, null, OutItem) == OutItem)
        {
            return true;
        }

        return Items.TryDequeue(out OutItem);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Enqueue(T Object)
    {
        if (Interlocked.CompareExchange(ref FastItem, Object, null) != null)
        {
            Items.Enqueue(Object);
        }
    }
}