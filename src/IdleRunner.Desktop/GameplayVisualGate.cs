using System.Diagnostics;
using IdleRunner.Core;

namespace IdleRunner.Desktop;

public sealed class GameplayVisualGate : IAutomationGate, IDisposable
{
    private static readonly long CacheDurationTicks = Stopwatch.Frequency / 4;
    private readonly IGameFrameSource _frameSource;
    private readonly GameplayCalibrationStore _store;
    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private VisualObservationDefinition? _observation;
    private AutomationGateResult? _lastResult;
    private long _lastCheckTimestamp;
    private int _isMinigameActive;
    private bool _disposed;

    public GameplayVisualGate(
        IGameFrameSource frameSource,
        GameplayCalibrationStore store)
    {
        _frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _observation = store.Load()?.CreateObservation();
    }

    public bool HasCalibration => Volatile.Read(ref _observation) is not null;

    public void SetMinigameActive(bool isActive)
    {
        Interlocked.Exchange(ref _isMinigameActive, isActive ? 1 : 0);
        _lastResult = null;
        _lastCheckTimestamp = 0;
    }

    public async Task CalibrateAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        GameFrame frame = await _frameSource.CaptureAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("A game frame could not be captured for calibration.");

        LuminanceRange range = frame.MeasureLuminanceRange(sampleStep: 8);
        if (range.Span < 16)
        {
            throw new InvalidOperationException(
                $"The captured game frame has insufficient contrast ({range.Darkest}-{range.Lightest}).");
        }

        GameplayHudCalibration calibration = GameplayHudCalibration.FromFrame(frame);
        _store.Save(calibration);
        Volatile.Write(ref _observation, calibration.CreateObservation());
        _lastResult = null;
        _lastCheckTimestamp = 0;
    }

    public async Task<AutomationGateResult> CheckAsync(
        GameSnapshot snapshot,
        AutomationSettings settings,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Volatile.Read(ref _isMinigameActive) != 0)
        {
            return AutomationGateResult.Blocked(
                "A minigame route is active; normal gameplay actions are blocked.");
        }

        VisualObservationDefinition? observation = Volatile.Read(ref _observation);
        if (observation is null)
        {
            return AutomationGateResult.Allowed("Visual safety gate is not calibrated.");
        }

        long now = Stopwatch.GetTimestamp();
        AutomationGateResult? cachedResult = _lastResult;
        if (cachedResult is not null && now - _lastCheckTimestamp < CacheDurationTicks)
        {
            return cachedResult;
        }

        await _checkLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = Stopwatch.GetTimestamp();
            cachedResult = _lastResult;
            if (cachedResult is not null && now - _lastCheckTimestamp < CacheDurationTicks)
            {
                return cachedResult;
            }

            GameFrame? frame = await _frameSource.CaptureAsync(cancellationToken).ConfigureAwait(false);
            AutomationGateResult result;

            if (frame is null)
            {
                result = AutomationGateResult.Blocked(
                    "A game frame could not be captured; normal gameplay actions are blocked for safety.");
            }
            else
            {
                VisualObservationResult observationResult = observation.Evaluate(frame);
                result = observationResult.IsMatch
                    ? AutomationGateResult.Allowed("Normal gameplay HUD recognized.")
                    : AutomationGateResult.Blocked(
                        "Normal gameplay HUD was not recognized; normal gameplay actions are blocked.");
            }

            _lastResult = result;
            _lastCheckTimestamp = Stopwatch.GetTimestamp();
            return result;
        }
        finally
        {
            _checkLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _checkLock.Dispose();
    }
}
