namespace Astral.UnitTests.TesterTools;

public sealed class AsyncScopeLock : IAsyncDisposable
{
    private static readonly SemaphoreSlim Semaphore = new(1, 1);
    private bool _acquired;

    private AsyncScopeLock() { }

    // Acquire the lock asynchronously
    public static async Task<AsyncScopeLock> LockAsync()
    {
        var scope = new AsyncScopeLock();
        await Semaphore.WaitAsync().ConfigureAwait(false);
        scope._acquired = true;
        return scope;
    }

    // Release the lock
    public ValueTask DisposeAsync()
    {
        if (_acquired)
        {
            _acquired = false;
            Semaphore.Release();
        }
        return ValueTask.CompletedTask;
    }
}