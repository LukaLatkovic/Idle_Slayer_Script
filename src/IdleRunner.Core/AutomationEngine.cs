namespace IdleRunner.Core;

public sealed class AutomationEngine
{
    private readonly IGameAdapter _gameAdapter;
    private readonly IAutomationLog _log;
    private readonly Func<AutomationSettings> _settingsProvider;
    private readonly object _statusLock = new();
    private int _isPaused = 1;
    private string? _lastStatus;

    public AutomationEngine(
        IGameAdapter gameAdapter,
        IAutomationLog log,
        Func<AutomationSettings> settingsProvider)
    {
        _gameAdapter = gameAdapter ?? throw new ArgumentNullException(nameof(gameAdapter));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
    }

    public event EventHandler<GameSnapshot>? SnapshotChanged;

    public event EventHandler<bool>? PauseChanged;

    public event EventHandler<AutomationAction>? ActionExecuted;

    public bool IsPaused => Volatile.Read(ref _isPaused) == 1;

    public void SetPaused(bool isPaused)
    {
        int nextValue = isPaused ? 1 : 0;
        int previousValue = Interlocked.Exchange(ref _isPaused, nextValue);

        if (previousValue == nextValue)
        {
            return;
        }

        _log.Info(isPaused ? "Automation paused." : "Automation started.");
        PauseChanged?.Invoke(this, isPaused);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _log.Info("Automation engine initialized in the paused state.");

        while (!cancellationToken.IsCancellationRequested)
        {
            AutomationSettings settings = _settingsProvider().Normalize();

            try
            {
                await RunOnceAsync(settings, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _log.Error("The automation cycle failed.", exception);
            }

            int delayMilliseconds = IsPaused
                ? 250
                : settings.JumpIntervalMilliseconds;

            try
            {
                await Task.Delay(delayMilliseconds, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        _log.Info("Automation engine stopped.");
    }

    public async Task<GameSnapshot> RunOnceAsync(
        AutomationSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        AutomationSettings normalizedSettings = (settings ?? _settingsProvider()).Normalize();
        GameSnapshot snapshot = await _gameAdapter
            .GetSnapshotAsync(cancellationToken)
            .ConfigureAwait(false);

        SnapshotChanged?.Invoke(this, snapshot);
        LogStatusChange(snapshot.Describe(normalizedSettings));

        if (!IsPaused && snapshot.CanReceiveInput(normalizedSettings))
        {
            bool wasExecuted = await _gameAdapter
                .ExecuteAsync(AutomationAction.JumpAndBoost, cancellationToken)
                .ConfigureAwait(false);

            if (wasExecuted)
            {
                ActionExecuted?.Invoke(this, AutomationAction.JumpAndBoost);
            }
        }

        return snapshot;
    }

    private void LogStatusChange(string status)
    {
        lock (_statusLock)
        {
            if (string.Equals(_lastStatus, status, StringComparison.Ordinal))
            {
                return;
            }

            _lastStatus = status;
        }

        _log.Info(status);
    }
}
