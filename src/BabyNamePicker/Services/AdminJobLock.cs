namespace BabyNamePicker.Services;

/// <summary>
/// Ensures only one LLM admin job runs at a time.
/// </summary>
public sealed class AdminJobLock
{
    private int _busy;
    private string? _currentJob;

    public bool TryAcquire(string jobName, out string? conflict)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) == 0)
        {
            _currentJob = jobName;
            conflict = null;
            return true;
        }

        conflict = _currentJob ?? "another job";
        return false;
    }

    public void Release()
    {
        _currentJob = null;
        Interlocked.Exchange(ref _busy, 0);
    }

    public bool IsBusy => Volatile.Read(ref _busy) == 1;
    public string? CurrentJob => _currentJob;
}
