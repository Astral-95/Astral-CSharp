using Astral.Tick;
using Astral.UnitTests.TesterTools;
using Xunit.Abstractions;

namespace Astral.UnitTests.Tools;

#pragma warning disable xUnit1013

[Collection("DisableParallelizationCollection")]
public class ParallelTickManagerTests
{
    private readonly ITestOutputHelper Output;

    string? WarnMsg = null;
    string? FaultMsg = null;

    public ParallelTickManagerTests(ITestOutputHelper Output)
    {
        ParallelTickManager.Initialize();
        this.Output = Output;

        ParallelTickManager.OnWarn = Msg => WarnMsg = Msg;
        ParallelTickManager.OnFault = Msg => FaultMsg = Msg;
    }

    //[Fact]
    //public async Task StartAsync()
    //{
    //	await CanRegisterAndUnregisterActionAsync();
    //	await DuplicateRegisterWarnsInDebugAsync();
    //	await UnregisterNonexistentIdTriggersWarnOrErrorAsync();
    //	await TickInvokesRegisteredActionsAsync();
    //	await InvalidActionMarkedForRemovalAsync();
    //	await WorkerLoopCatchesExceptionAsync();
    //	await TickRateChangeAdjustsIntervalAsync();
    //	await MultipleWorkersProcessAllActionsAsync();
    //	await SwapRemoveKeepsIntegrityAsync();
    //}
    async Task ResetAsync()
    {
        await Task.Delay(1000 / ParallelTickManager.TickRate + 25);
        ParallelTickManager.ResetForTests();
    }

    [Fact]
    public async Task CanRegisterAndUnregisterActionAsync()
    {
        await using var _ = await AsyncScopeLock.LockAsync();
        string? ErrorMsg = null;
        ParallelTickManager.OnFault = Msg => ErrorMsg = Msg;

        long Id = ParallelTickManager.Register(() => { });
        Assert.True(Id > 0);

        ParallelTickManager.Unregister(Id);

        var Handle = ParallelTickManager.RegisterParallelTick(() => { });
        Assert.True(Id > 0);
        ParallelTickManager.UnregisterParallelTick(Handle);

        Assert.True(ErrorMsg == null, ErrorMsg);
        await ResetAsync();
    }
    [Fact]
    public async Task DuplicateRegisterWarnsInDebugAsync()
    {
        string? WarnMsg = null;
#if !RELEASE
        ParallelTickManager.OnWarn = Msg => WarnMsg = Msg;

        long Id1 = ParallelTickManager.Register(() => { });
        long Id2 = ParallelTickManager.Register(() => { });
        ParallelTickManager.Unregister(Id1);
        ParallelTickManager.Unregister(Id2);

        var Handle1 = ParallelTickManager.RegisterParallelTick(() => { });
        var Handle2 = ParallelTickManager.RegisterParallelTick(() => { });
        ParallelTickManager.UnregisterParallelTick(Handle1);
        ParallelTickManager.UnregisterParallelTick(Handle2);
#endif
        await ResetAsync();
        Assert.True(WarnMsg == null, WarnMsg);
    }
    [Fact]
    public async Task UnregisterNonexistentIdTriggersWarnOrErrorAsync()
    {
        string? WarnMsg = null;

        ParallelTickManager.OnWarn = Msg => WarnMsg = Msg;

        ParallelTickManager.Unregister(-1);

        await ResetAsync();
        Assert.True(WarnMsg != null);
    }
    [Fact]
    public async Task TickInvokesRegisteredActionsAsync()
    {
        long Calls = 0;
        long Id = ParallelTickManager.Register(() =>
        {
            Interlocked.Increment(ref Calls);
        });

        await Task.Delay(100);
        ParallelTickManager.Unregister(Id);
        await ResetAsync();

        Assert.True(Calls > 0, "Tick was not called in time.");

        Calls = 0;
        var ParallelHandle = ParallelTickManager.RegisterParallelTick(() =>
        {
            Interlocked.Increment(ref Calls);
        });

        await Task.Delay(100);
        ParallelTickManager.UnregisterParallelTick(ParallelHandle);
        await ResetAsync();

        Assert.True(Calls > 0, "ParallelTick was not called in time.");
    }
    [Fact]
    public async Task InvalidActionMarkedForRemovalAsync()
    {
        var WarnMessage = null as string;

        ParallelTickManager.OnWarn = Msg => WarnMessage = Msg;
        ParallelTickManager.Unregister(9999999);
        await ResetAsync();
        Assert.NotNull(WarnMessage);
    }
    [Fact]
    public async Task WorkerLoopCatchesExceptionAsync()
    {
        string? ErrorMsg = null;
        ParallelTickManager.OnError = Msg => ErrorMsg = Msg;

        long Id = ParallelTickManager.Register(() => throw new InvalidOperationException("Test"));
        var PrallelHandle = ParallelTickManager.RegisterParallelTick(() => throw new InvalidOperationException("ParallelTest"));

        // Wait a few ticks for worker to process
        await Task.Delay(200);

        ParallelTickManager.Unregister(Id);
        ParallelTickManager.UnregisterParallelTick(PrallelHandle);
        await ResetAsync();

        // Should have triggered error handling
        Assert.NotNull(ErrorMsg);
    }
    [Fact]
    public async Task TickRateChangeAdjustsIntervalAsync()
    {
        ParallelTickManager.SetTickRate(1000);

        long Id = ParallelTickManager.Register(() => { });
        var Handle = ParallelTickManager.RegisterParallelTick(() => { });

        await Task.Delay(200);

        ParallelTickManager.Unregister(Id);
        ParallelTickManager.UnregisterParallelTick(Handle);

        ParallelTickManager.SetTickRate(60);
        await ResetAsync();
    }
    [Fact]
    public async Task MultipleWorkersProcessAllActionsAsync()
    {
        int NumActions = 50;
        int ExecutedCount = 0;
        var Ids = new List<long>();

        for (int i = 0; i < NumActions; i++)
        {
            long Id = ParallelTickManager.Register(() =>
            {
                Interlocked.Increment(ref ExecutedCount);
            });
            Ids.Add(Id);
        }

        await Task.Delay(300);

        foreach (var Id in Ids) ParallelTickManager.Unregister(Id);

        await ResetAsync();

        Assert.True(ExecutedCount >= NumActions);

        // TODO: Do the same ParallelTick
    }
    [Fact]
    public async Task SwapRemoveKeepsIntegrityAsync()
    {
        int CalledCount = 0;

        var Ids = new List<long>();
        for (int i = 0; i < 10; i++)
        {
            long id = ParallelTickManager.Register(() =>
            {
                Interlocked.Increment(ref CalledCount);
            });
            Ids.Add(id);
        }

        await Task.Delay(200);

        Assert.True(CalledCount > 0); // Ensure other actions still called

        foreach (var Id in Ids) ParallelTickManager.Unregister(Id);

        CalledCount = 0;

        var ParallelHandles = new List<ParallelTickHandle>();
        for (int i = 0; i < 10; i++)
        {
            var Handle = ParallelTickManager.RegisterParallelTick(() =>
            {
                Interlocked.Increment(ref CalledCount);
            });
            ParallelHandles.Add(Handle);
        }

        await Task.Delay(200);

        Assert.True(CalledCount > 0); // Ensure other actions still called

        foreach (var Handle in ParallelHandles) ParallelTickManager.UnregisterParallelTick(Handle);
        await ResetAsync();
    }
}