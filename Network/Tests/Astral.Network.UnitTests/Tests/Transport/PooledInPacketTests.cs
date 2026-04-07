using Astral.Network.Transport;
using Microsoft.Coyote;
using Microsoft.Coyote.SystematicTesting;

namespace Astral.Network.UnitTests.Transport;

public class PooledInPacketTests
{
    private static TestingEngine CreateEngine(Func<Task> test, uint iterations = 1000, uint maxSteps = 500)
    {
        var config = Configuration.Create()
            .WithTestingIterations(iterations)
            .WithMaxSchedulingSteps(maxSteps);
        return TestingEngine.Create(config, test);
    }

    private static void RunAndAssert(TestingEngine engine)
    {
        engine.Run();
        var report = engine.TestReport;
        Assert.True(
            report.NumOfFoundBugs == 0,
            $"Coyote found {report.NumOfFoundBugs} bug(s):\n{report.GetText(Configuration.Create())}\n{string.Join("\n", report.BugReports)}"
        );
    }

    [Fact]
    public void VerifyPoolIntegrity_Systematic()
    {
        var engine = CreateEngine(HammerLogic);
        RunAndAssert(engine);
    }

    [Fact]
    public void VerifyPoolIntegrity_HardwareLevel()
    {
        int threadCount = Environment.ProcessorCount; // Use all 12/24 cores
        int iterations = 100_000_000;
        var barriers = new Barrier(threadCount); // Synchronize start for a "Big Bang"

        var threads = new Thread[threadCount];
        for (int i = 0; i < threadCount; i++)
        {
            threads[i] = new Thread(() =>
            {
                barriers.SignalAndWait(); // All threads start at the exact same moment
                for (int j = 0; j < iterations; j++)
                {
                    var packet = PooledInPacket.Rent<PooledInPacketTests>();
                    // Logic check: If your pool fails, packet is often null or has a corrupt ID
                    Assert.NotNull(packet);
                    packet.Return();
                }
            });
            threads[i].Start();
        }

        foreach (var t in threads) t.Join();
    }


    private static async Task HammerLogic()
    {
        int workerCount = 10;
        Task[] workers = new Task[workerCount];

        for (int i = 0; i < workerCount; i++)
        {
            workers[i] = ChaoticWorker(10); // Higher iterations to ensure overlap
        }

        await Task.WhenAll(workers);
    }

    private static async Task ChaoticWorker(int totalActions)
    {
        var heldPackets = new Stack<PooledInPacket>();

        for (int i = 0; i < totalActions; i++)
        {
            // Use Coyote's systematic random to decide: Rent or Return?
            // This ensures the "interleaving" is recorded and reproducible.
            bool shouldRent = Microsoft.Coyote.Random.Generator.Create().NextBoolean();

            if (shouldRent || heldPackets.Count == 0)
            {
                var packet = PooledInPacket.Rent<PooledInPacketTests>();
                if (packet != null)
                {
                    heldPackets.Push(packet);
                }
            }
            else
            {
                var packet = heldPackets.Pop();
                packet.Return();
            }

            // Randomly yield to let other threads "steal" from the ConcurrentBag
            // during mid-operation state changes.
            if (Microsoft.Coyote.Random.Generator.Create().NextBoolean())
            {
                await Task.Yield();
            }
        }

        // Cleanup remaining
        while (heldPackets.Count > 0)
        {
            heldPackets.Pop().Return();
        }
    }
}