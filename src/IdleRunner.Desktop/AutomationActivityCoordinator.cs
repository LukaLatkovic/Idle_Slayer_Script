namespace IdleRunner.Desktop;

public sealed class AutomationActivityCoordinator
{
    private readonly Action<bool> _setNormalInputBlocked;
    private int _isExclusiveActionActive;

    public AutomationActivityCoordinator(Action<bool> setNormalInputBlocked)
    {
        _setNormalInputBlocked = setNormalInputBlocked
            ?? throw new ArgumentNullException(nameof(setNormalInputBlocked));
    }

    public bool IsExclusiveActionActive => Volatile.Read(ref _isExclusiveActionActive) != 0;

    public bool TryEnter()
    {
        if (Interlocked.CompareExchange(ref _isExclusiveActionActive, 1, 0) != 0)
        {
            return false;
        }

        _setNormalInputBlocked(true);
        return true;
    }

    public void Exit()
    {
        if (Interlocked.Exchange(ref _isExclusiveActionActive, 0) == 0)
        {
            return;
        }

        _setNormalInputBlocked(false);
    }
}
