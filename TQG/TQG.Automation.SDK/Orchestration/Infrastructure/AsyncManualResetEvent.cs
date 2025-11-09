namespace TQG.Automation.SDK.Orchestration.Infrastructure;

/// <summary>
/// Async-compatible manual reset event for pause/resume gate synchronization.
/// Allows multiple awaiters to be signaled simultaneously.
/// </summary>
internal sealed class AsyncManualResetEvent
{
    private volatile TaskCompletionSource<bool> _tcs;

    /// <summary>
    /// Initializes a new instance with the specified initial state.
    /// </summary>
    /// <param name="initialState">True if initially set (open gate); false if reset (closed gate).</param>
    public AsyncManualResetEvent(bool initialState = false)
    {
        _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (initialState)
        {
            _tcs.SetResult(true);
        }
    }

    /// <summary>
    /// Asynchronously waits for the event to be set.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the wait.</param>
    /// <returns>Task that completes when the event is set.</returns>
    public Task WaitAsync(CancellationToken cancellationToken = default)
    {
        return _tcs.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Sets the event, allowing all awaiting tasks to proceed.
    /// </summary>
    public void Set()
    {
        _tcs.TrySetResult(true);
    }

    /// <summary>
    /// Resets the event, causing subsequent waits to block.
    /// </summary>
    public void Reset()
    {
        while (true)
        {
            var tcs = _tcs;

            // If not completed, nothing to reset
            if (!tcs.Task.IsCompleted)
            {
                return;
            }

            // Try to replace with a new TCS
            var newTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (Interlocked.CompareExchange(ref _tcs, newTcs, tcs) == tcs)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Gets whether the event is currently set.
    /// </summary>
    public bool IsSet => _tcs.Task.IsCompleted;
}
