using Astral.Containers;
using Astral.UnitTests.TesterTools;

namespace Astral.UnitTests.Pools;

[Collection("DisableParallelizationCollection")]
public class ListPoolTests
{
    private static T GetRandomValue<T>() where T : unmanaged
    {
        if (typeof(T) == typeof(int)) return (T)(object)Random.Shared.Next();
        if (typeof(T) == typeof(byte)) return (T)(object)(byte)Random.Shared.Next(0, 256);
        if (typeof(T) == typeof(short)) return (T)(object)(short)Random.Shared.Next(short.MinValue, short.MaxValue);
        if (typeof(T) == typeof(long)) return (T)(object)(long)Random.Shared.NextInt64();
        if (typeof(T) == typeof(float)) return (T)(object)((float)Random.Shared.NextDouble());
        if (typeof(T) == typeof(double)) return (T)(object)Random.Shared.NextDouble();

        throw new NotSupportedException($"Random generation for type {typeof(T)} is not supported.");
    }

    private static async Task RunListPoolIteration<T>() where T : unmanaged
    {
        var RentedList = PooledList<T>.Rent();

        for (int i = 0; i < 10; i++)
        {
            var Value = GetRandomValue<T>();
            RentedList.Add(Value);

            if (Random.Shared.Next(5) == 0)
            {
                RentedList.Remove(Value);
            }
        }

        RentedList.Return();

        await Task.Delay(Random.Shared.Next(0, 5));
    }

    private static async Task HammerPool<T>(int Iterations = 100000) where T : unmanaged
    {
        var Tasks = new Task[Iterations];
        for (int i = 0; i < Iterations; i++)
        {
            Tasks[i] = RunListPoolIteration<T>();
        }

        await Task.WhenAll(Tasks);
    }

    [Fact]
    public async Task ListPoolTest()
    {
        await using var _ = await AsyncScopeLock.LockAsync();
        PooledObjectsTracker.ClearForTests();
        await HammerPool<int>();
        await HammerPool<float>();
        await HammerPool<double>();

        var Leaks = PooledObjectsTracker.ReportLeaks();

        if (Leaks != null) Assert.Fail(string.Join("\n", Leaks));
    }
}