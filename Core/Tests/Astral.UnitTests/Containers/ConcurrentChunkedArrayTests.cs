using Astral.Containers;

namespace Astral.UnitTests.Containers;

public class ConcurrentChunkedArrayTests
{
    [Fact]
    public void AddAndIndexerSequential_WorksCorrectly()
    {
        var array = new ConcurrentChunkedArray<int>(ChunkSize: 3);
        array.Add(1);
        array.Add(2);
        array.Add(3);
        array.Add(4); // triggers new chunk

        Assert.Equal(4, array.Count);
        Assert.Equal(1, array[0]);
        Assert.Equal(2, array[1]);
        Assert.Equal(3, array[2]);
        Assert.Equal(4, array[3]);

        array[2] = 99;
        Assert.Equal(99, array[2]);
    }

    [Fact]
    public void Enumerator_YieldsAllItems()
    {
        var array = new ConcurrentChunkedArray<int>(ChunkSize: 2);
        for (int i = 0; i < 5; i++)
            array.Add(i);

        var list = array.ToList();
        Assert.Equal(5, list.Count);
        for (int i = 0; i < 5; i++)
            Assert.Equal(i, list[i]);
    }

    [Fact]
    public void ForEach_IteratesCorrectly()
    {
        var array = new ConcurrentChunkedArray<int>(ChunkSize: 2);
        for (int i = 0; i < 5; i++)
            array.Add(i);

        var collected = new List<int>();
        array.ForEach(x => collected.Add(x));

        Assert.Equal(5, collected.Count);
        for (int i = 0; i < 5; i++)
            Assert.Equal(i, collected[i]);
    }

    [Fact]
    public void Indexer_OutOfRange_Throws()
    {
        var array = new ConcurrentChunkedArray<int>(ChunkSize: 2);
        array.Add(1);

        Assert.Throws<IndexOutOfRangeException>(() => _ = array[1]);
        Assert.Throws<IndexOutOfRangeException>(() => array[1] = 5);
        Assert.Throws<IndexOutOfRangeException>(() => _ = array[-1]);
    }

    [Fact]
    public void Count_ReflectsAllItems()
    {
        var array = new ConcurrentChunkedArray<int>(ChunkSize: 3);
        Assert.Equal(0, array.Count);

        for (int i = 0; i < 7; i++)
            array.Add(i);

        Assert.Equal(7, array.Count);
    }

    [Fact]
    public void Concurrent_Adds_DoNotCorruptData()
    {
        var array = new ConcurrentChunkedArray<int>(ChunkSize: 100);
        int total = 10000;

        Parallel.For(0, total, i =>
        {
            array.Add(i);
        });

        Assert.Equal(total, array.Count);

        var all = array.ToList();
        var missing = Enumerable.Range(0, total).Except(all).ToList();
        Assert.Empty(missing); // all numbers should be present
    }

    [Fact]
    public void Concurrent_ReadsAndWrites_AreSafe()
    {
        var array = new ConcurrentChunkedArray<int>(ChunkSize: 50);
        int total = 1000;

        Parallel.Invoke(
            () =>
            {
                for (int i = 0; i < total; i++)
                    array.Add(i);
            },
            () =>
            {
                int attempts = 0;
                while (attempts < 100)
                {
                    int count = array.Count;
                    if (count > 0)
                    {
                        var _ = array[count - 1];
                    }
                    attempts++;
                }
            }
        );

        Assert.True(array.Count <= total);
    }

    [Fact]
    public async Task Enumerator_ConcurrentModification_DoesNotCrashAsync()
    {
        var array = new ConcurrentChunkedArray<int>(ChunkSize: 10);
        for (int i = 0; i < 50; i++) array.Add(i);

        var task1 = Task.Run(() =>
        {
            for (int i = 50; i < 100; i++)
                array.Add(i);
        });

        var task2 = Task.Run(() =>
        {
            foreach (var item in array)
            {
                // just iterate
            }
        });

        await Task.WhenAll(task1, task2);

        Assert.Equal(100, array.Count);
    }


    [Fact]
    public async Task HighStressConcurrentOperationsAsync()
    {
        var Array = new ConcurrentChunkedArray<int>(ChunkSize: 100);
        int TotalThreads = 200;
        int ItemsPerThread = 500;

        var Tasks = new List<Task>();

        for (int t = 0; t < TotalThreads; t++)
        {
            int ThreadId = t;
            Tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < ItemsPerThread; i++)
                {
                    Array.Add(ThreadId * ItemsPerThread + i);
                }
            }));
        }

        for (int t = 0; t < TotalThreads; t++)
        {
            Tasks.Add(Task.Run(() =>
            {
                Random Rnd = new Random();
                for (int i = 0; i < ItemsPerThread; i++)
                {
                    int Count = Array.Count;
                    if (Count == 0) continue;

                    int Attempt = 0;
                    bool Success = false;
                    while (!Success && Attempt < 5)
                    {
                        int Index = Rnd.Next(Count);
                        if (Array.TryGet(Index, out int Value))
                        {
                            Success = Array.TrySet(Index, Value + 1);
                        }
                        Attempt++;
                    }

                    Array.ForEach(x => { var _ = x; });
                }
            }));
        }

        while (Tasks.Count > 0)
        {
            var FinishedTask = await Task.WhenAny(Tasks);
            if (FinishedTask.IsFaulted) await FinishedTask;
            Tasks.Remove(FinishedTask);
        }

        int ExpectedCount = TotalThreads * ItemsPerThread;
        Assert.Equal(ExpectedCount, Array.Count);

        var AllItems = Array.ToList();
        var Missing = Enumerable.Range(0, ExpectedCount)
                                .Except(AllItems.Select(x => x / 1))
                                .ToList();
        Assert.Empty(Missing);

        int IteratedCount = 0;
        Array.ForEach(_ => IteratedCount++);
        Assert.Equal(Array.Count, IteratedCount);
    }
}