using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Astral.Containers;

class ConcurrentFastStore<T, TKey> where T : class
{
#pragma warning disable CS0169
    // --- Group 1: The Queue ---
    // Padding before the queue
    private readonly long p1, p2, p3, p4, p5, p6, p7, p8;
    private readonly ConcurrentQueue<T> Items = new();
    // Padding after the queue
    private readonly long p9, p10, p11, p12, p13, p14, p15, p16;

    // --- Group 2: The Thread-Local ---
    // (ThreadStatic doesn't need padding because it lives in a different memory area entirely)
    [ThreadStatic]
    private static T? LocalItem;

    // --- Group 3: The Shared Slot ---
    // Padding before the shared item
    private readonly long p17, p18, p19, p20, p21, p22, p23, p24;
    private T? SharedItem;
    // Padding after the shared item
    private readonly long p25, p26, p27, p28, p29, p30, p31, p32;
#pragma warning restore CS0169

    /// <summary>
    /// +- 1
    /// </summary>
    public int Count => Items.Count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? Take()
    {
        T? Item;
        if (LocalItem != null)
        {
            Item = LocalItem;
            LocalItem = null;
            return Item;
        }

        Item = SharedItem;
        if (Item == null || Interlocked.CompareExchange(ref SharedItem, null, Item) != Item)
        {
            Items.TryDequeue(out Item);
            return Item;
        }

        return Item;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Take([MaybeNullWhen(false)] out T Item)
    {
        Item = LocalItem;
        if (Item != null)
        {
            LocalItem = null;
            return true;
        }

        Item = SharedItem;
        if (Item == null || Interlocked.CompareExchange(ref SharedItem, null, Item) != Item)
        {
            return Items.TryDequeue(out Item);
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T Obj)
    {
        if (LocalItem == null)
        {
            LocalItem = Obj;
            return;
        }

        if (Interlocked.CompareExchange(ref SharedItem, Obj, null) == null)
        {
            return;
        }

        Items.Enqueue(Obj);
    }
}