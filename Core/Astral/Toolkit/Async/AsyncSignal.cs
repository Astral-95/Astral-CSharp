namespace Astral.Async;

public interface ICancellationMode
{
    ValueTask WaitAsync(Task TcsTask, CancellationToken Token);
}

// Shared cancellation: all waiters observe the same external token (cancels all waiters at once)
public struct SingleToken : ICancellationMode
{
    public ValueTask WaitAsync(Task tcsTask, CancellationToken token)
    {
        // Local copy avoids races if the caller swaps the underlying TCS concurrently
        var task = tcsTask;

        // Fast path: token cannot cancel -> just await the task (no extra alloc)
        if (!token.CanBeCanceled)
            return new ValueTask(task);

        // Fast path: task already completed (success/fault/cancel) -> return it to propagate result/exception
        if (task.IsCompleted)
            return new ValueTask(task);

        // Slow path: need to race task vs cancellation; allocate only here
        return new ValueTask(WaitWithCancelAsync(task, token));
    }

    private static async Task WaitWithCancelAsync(Task task, CancellationToken token)
    {
        var CancelTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var Reg = token.Register(static s => ((TaskCompletionSource<object?>)s!).TrySetResult(null), CancelTcs);

        var Completed = await Task.WhenAny(task, CancelTcs.Task).ConfigureAwait(false);

        if (Completed != task)
            throw new OperationCanceledException(token);

        // Propagate any exception/cancellation from the original task
        await task.ConfigureAwait(false);
    }
}

// Per-wait cancellation: every waiter supplies its own token
public struct MultiToken : ICancellationMode
{
    public ValueTask WaitAsync(Task tcsTask, CancellationToken token)
    {
        var task = tcsTask;

        if (!token.CanBeCanceled)
            return new ValueTask(task);

        if (task.IsCompleted)
            return new ValueTask(task);

        return new ValueTask(WaitWithCancelAsync(task, token));
    }

    private static async Task WaitWithCancelAsync(Task task, CancellationToken token)
    {
        var CancelTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var Reg = token.Register(static s => ((TaskCompletionSource<object?>)s!).TrySetResult(null), CancelTcs);

        var Completed = await Task.WhenAny(task, CancelTcs.Task).ConfigureAwait(false);

        if (Completed != task)
            throw new OperationCanceledException(token);

        await task.ConfigureAwait(false);
    }
}

public class AsyncSignal<T> where T : struct, ICancellationMode
{
    // Volatile so threads always see latest reference
    private volatile TaskCompletionSource<bool> _Tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Mode is a struct; Default(T) is fine for stateless modes
    private readonly T Mode = new();

    public ValueTask WaitAsync(CancellationToken token = default) =>
        Mode.WaitAsync(_Tcs.Task, token);

    public void Release()
    {
        // Complete current TCS. If already completed, fine.
        _Tcs.TrySetResult(true);
    }

    public void Reset()
    {
        while (true)
        {
            var Old = _Tcs;

            // If current is not completed, nothing to do
            if (!Old.Task.IsCompleted)
                return;

            var New = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (Interlocked.CompareExchange(ref _Tcs, New, Old) == Old)
                return;

            // else someone swapped it first; loop and retry
        }
    }
}

// Convenience non-generic alias using SingleToken (shared cancellation semantics)
public class AsyncSignal : AsyncSignal<SingleToken> { }