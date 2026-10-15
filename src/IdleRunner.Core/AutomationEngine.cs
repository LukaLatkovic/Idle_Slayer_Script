using System.Diagnostics;

namespace IdleRunner.Core;

public sealed class AutomationEngine
{
    private readonly IGameAdapter _gameAdapter;
    private readonly IAutomationLog _log;
    private readonly Func<AutomationSettings> _settingsProvider;
    private readonly IReadOnlyList<IAutomationModule> _modules;
    private readonly IAutomationGate? _gate;
    private readonly Dictionary<IAutomationModule, long> _nextRunTimestamps = new();
    private readonly object _statusLock = new();
    private int _isPaused = 1;
    private string? _lastStatus;

    public AutomationEngine(
        IGameAdapter gameAdapter,
        IAutomationLog log,
        Func<AutomationSettings> settingsProvider,
        IEnumerable<IAutomationModule>? modules = null,
        IAutomationGate? gate = null)
    {
        _gameAdapter = gameAdapter ?? throw new ArgumentNullException(nameof(gameAdapter));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        _modules = (modules ?? [new JumpAndBoostModule()]).ToArray();
        _gate = gate;

        if (_modules.Count == 0)
        {
            throw new ArgumentException("At least one automation module is required.", nameof(modules));
        }
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

        _log.Info(isPaused ? "Automation stopped by the user." : "Automation started.");
        PauseChanged?.Invoke(this, isPaused);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _log.Info("Automation engine initialized in the stopped state.");

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

            int delayMilliseconds = IsPaused ? 250 : 25;

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

        if (IsPaused || !snapshot.CanReceiveInput(normalizedSettings))
        {
            LogStatusChange(snapshot.Describe(normalizedSettings));
            return snapshot;
        }

        if (_gate is not null)
        {
            AutomationGateResult gateResult = await _gate
                .CheckAsync(snapshot, normalizedSettings, cancellationToken)
                .ConfigureAwait(false);

            if (!gateResult.AllowsActions)
            {
                LogStatusChange(gateResult.Description);
                return snapshot;
            }
        }

        LogStatusChange(snapshot.Describe(normalizedSettings));

        long now = Stopwatch.GetTimestamp();

        foreach (IAutomationModule module in _modules)
        {
            if (_nextRunTimestamps.TryGetValue(module, out long nextRun)
                && now < nextRun)
            {
                continue;
            }

            bool canRun = module is IObservedAutomationModule observedModule
                ? await observedModule
                    .CanRunAsync(snapshot, normalizedSettings, cancellationToken)
                    .ConfigureAwait(false)
                : module.CanRun(snapshot, normalizedSettings);

            TimeSpan interval = module.GetInterval(normalizedSettings);
            long intervalTicks = (long)(interval.TotalSeconds * Stopwatch.Frequency);
            _nextRunTimestamps[module] = Stopwatch.GetTimestamp() + Math.Max(1, intervalTicks);

            if (!canRun)
            {
                continue;
            }

            bool wasExecuted = await _gameAdapter
                .ExecuteAsync(module.Action, cancellationToken)
                .ConfigureAwait(false);

            if (wasExecuted)
            {
                ActionExecuted?.Invoke(this, module.Action);
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
