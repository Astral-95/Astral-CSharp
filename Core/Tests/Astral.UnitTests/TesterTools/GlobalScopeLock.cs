namespace Astral.UnitTests.TesterTools;

internal sealed class GlobalScopeLock : IDisposable
{
    static object Lock = new();

    public GlobalScopeLock() => Monitor.Enter(Lock);

    public void Dispose() => Monitor.Exit(Lock);
}