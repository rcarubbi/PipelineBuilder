namespace PipelineBuilder.Infrastructure.JobHandlers;

public sealed class PipelineWorkerState
{
    private int _running;
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool IsRunning => Volatile.Read(ref _running) == 1;
    public Task Started => _started.Task;

    public bool TryStart()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return false;
        _started.TrySetResult();
        return true;
    }

    public void Stop() => Volatile.Write(ref _running, 0);
}
