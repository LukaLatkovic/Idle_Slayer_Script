using IdleRunner.Core;

namespace IdleRunner.Desktop;

public sealed class ChestHuntAutomationService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan OutcomePollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan DoubleRewardDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan EndScreenTimeout = TimeSpan.FromSeconds(12);
    private const int PerfectChestClickX = 457;
    private const int PerfectChestClickY = 439;
    private const int CloseButtonClickX = 643;
    private const int CloseButtonClickY = 693;

    private readonly IGameAdapter _gameAdapter;
    private readonly IGamePointerAdapter _pointerAdapter;
    private readonly IGameFrameSource _frameSource;
    private readonly ChestHuntVisualService _visualService;
    private readonly IAutomationLog _log;
    private readonly Func<AutomationSettings> _settingsProvider;
    private readonly Func<bool> _isPaused;
    private readonly Action _pauseAutomation;
    private readonly Action _clickExecuted;
    private bool _reportedUnsafeResume;
    private bool _waitingForGridExit;

    public ChestHuntAutomationService(
        IGameAdapter gameAdapter,
        IGamePointerAdapter pointerAdapter,
        IGameFrameSource frameSource,
        ChestHuntVisualService visualService,
        IAutomationLog log,
        Func<AutomationSettings> settingsProvider,
        Func<bool> isPaused,
        Action pauseAutomation,
        Action clickExecuted)
    {
        _gameAdapter = gameAdapter ?? throw new ArgumentNullException(nameof(gameAdapter));
        _pointerAdapter = pointerAdapter ?? throw new ArgumentNullException(nameof(pointerAdapter));
        _frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
        _visualService = visualService ?? throw new ArgumentNullException(nameof(visualService));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        _isPaused = isPaused ?? throw new ArgumentNullException(nameof(isPaused));
        _pauseAutomation = pauseAutomation ?? throw new ArgumentNullException(nameof(pauseAutomation));
        _clickExecuted = clickExecuted ?? throw new ArgumentNullException(nameof(clickExecuted));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _log.Error("The Chest Hunt automation cycle failed.", exception);
                PauseForSafety("Chest Hunt automation was paused after an unexpected error.");
                _waitingForGridExit = true;
            }

            try
            {
                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (_isPaused() || !_visualService.HasCalibration)
        {
            return;
        }

        AutomationSettings settings = _settingsProvider().Normalize();
        GameSnapshot snapshot = await _gameAdapter
            .GetSnapshotAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!snapshot.CanReceiveInput(settings))
        {
            return;
        }

        ChestHuntFrameObservation? observation = await _visualService
            .ObserveAsync(cancellationToken)
            .ConfigureAwait(false);
        if (observation is null || !observation.IsActive)
        {
            _reportedUnsafeResume = false;
            _waitingForGridExit = false;
            return;
        }

        if (_waitingForGridExit)
        {
            return;
        }

        if (!observation.IsFresh)
        {
            if (!_reportedUnsafeResume)
            {
                _reportedUnsafeResume = true;
                _log.Info(
                    $"Chest Hunt detected with {observation.OpenedNonSaverCount} chest(s) already open. "
                    + "Waiting for a fresh Chest Hunt because earlier Mimic outcomes cannot be reconstructed safely.");
            }

            return;
        }

        _reportedUnsafeResume = false;
        _log.Info(
            $"Fresh Chest Hunt detected. Saver is at row {observation.Saver.Position.Row}, "
            + $"column {observation.Saver.Position.Column}.");

        bool completed = await PlayAsync(
            observation.Saver.Position,
            settings,
            cancellationToken).ConfigureAwait(false);
        _waitingForGridExit = completed;
    }

    private async Task<bool> PlayAsync(
        ChestHuntGridPosition saverPosition,
        AutomationSettings settings,
        CancellationToken cancellationToken)
    {
        ChestHuntCalibration? calibration = _visualService.Calibration;
        if (calibration is null)
        {
            return false;
        }

        ChestHuntTimingProfile timing = ChestHuntTimingProfile.FromSettings(settings);
        if (!await DelayWhileRunningAsync(timing.InitialDelay, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        ChestHuntFrameObservation? readyObservation = await _visualService
            .ObserveAsync(cancellationToken)
            .ConfigureAwait(false);
        if (readyObservation is null
            || !readyObservation.IsFresh
            || readyObservation.Saver.Position != saverPosition)
        {
            PauseForSafety("Chest Hunt changed before execution started; automation was paused for safety.");
            return true;
        }

        ChestHuntSession session = new(saverPosition);

        while (!session.IsComplete)
        {
            ChestHuntGridPosition? nextPosition = session.NextPosition;
            if (nextPosition is null || _isPaused())
            {
                return false;
            }

            GameFrame? beforeClick = await _frameSource
                .CaptureAsync(cancellationToken)
                .ConfigureAwait(false);
            bool targetIsReady = false;
            if (beforeClick is not null)
            {
                if (session.IsOpeningLifeSaver)
                {
                    ChestHuntSaverMatch saver = calibration.LocateSaver(beforeClick);
                    targetIsReady = saver.IsMatch && saver.Position == nextPosition.Value;
                }
                else
                {
                    targetIsReady = calibration.IsClosedChest(beforeClick, nextPosition.Value);
                }
            }

            if (!targetIsReady)
            {
                PauseForSafety(
                    $"Chest Hunt target at row {nextPosition.Value.Row}, "
                    + $"column {nextPosition.Value.Column} was not in the expected state; "
                    + "automation was paused before clicking.");
                return true;
            }

            if (!await CanClickAsync(settings, cancellationToken).ConfigureAwait(false)
                || !await _pointerAdapter.ClickClientPointAsync(
                    nextPosition.Value.CenterX,
                    nextPosition.Value.CenterY,
                    cancellationToken).ConfigureAwait(false))
            {
                PauseForSafety("Chest Hunt click was rejected because the game is not ready for input.");
                return true;
            }

            _clickExecuted();

            if (!await DelayWhileRunningAsync(timing.ChestRevealDelay, cancellationToken)
                    .ConfigureAwait(false))
            {
                return false;
            }

            GameFrame? frame = await _frameSource.CaptureAsync(cancellationToken).ConfigureAwait(false);
            if (frame is null)
            {
                PauseForSafety("Chest Hunt result could not be captured; automation was paused for safety.");
                return true;
            }

            ChestHuntOutcome outcome;
            if (session.IsOpeningLifeSaver)
            {
                outcome = calibration.IsSaverVisible(frame)
                    ? ChestHuntOutcome.Unknown
                    : ChestHuntOutcome.LifeSaver;
            }
            else
            {
                outcome = calibration.DetectOutcome(frame, nextPosition.Value);
            }

            if (outcome == ChestHuntOutcome.Mimic)
            {
                if (!await DelayWhileRunningAsync(timing.MimicRevealDelay, cancellationToken)
                        .ConfigureAwait(false))
                {
                    return false;
                }

                frame = await _frameSource.CaptureAsync(cancellationToken).ConfigureAwait(false);
                if (frame is null)
                {
                    PauseForSafety("The Mimic result could not be verified; automation was paused for safety.");
                    return true;
                }

                if (calibration.IsHuntEndVisible(frame))
                {
                    outcome = ChestHuntOutcome.HuntEnded;
                }
            }
            else if (outcome == ChestHuntOutcome.DoubleReward
                && !await DelayWhileRunningAsync(DoubleRewardDelay, cancellationToken)
                    .ConfigureAwait(false))
            {
                return false;
            }

            ChestHuntStep step = session.RecordOutcome(outcome, settings);
            _log.Info(
                $"Chest Hunt row {nextPosition.Value.Row}, column {nextPosition.Value.Column}: "
                + $"{outcome}. {step.Reason}");

            if (outcome == ChestHuntOutcome.Unknown)
            {
                PauseForSafety("Chest Hunt outcome was not recognized; no further chests will be opened.");
                return true;
            }

            if (outcome == ChestHuntOutcome.HuntEnded)
            {
                return await CloseResultScreenAsync(calibration, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return await CloseResultScreenAsync(calibration, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> CloseResultScreenAsync(
        ChestHuntCalibration calibration,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + EndScreenTimeout;
        bool perfectChestOpened = false;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (_isPaused())
            {
                return false;
            }

            GameFrame? frame = await _frameSource.CaptureAsync(cancellationToken).ConfigureAwait(false);
            if (frame is null)
            {
                break;
            }

            if (calibration.IsHuntEndVisible(frame))
            {
                bool clicked = await _pointerAdapter.ClickClientPointAsync(
                    CloseButtonClickX,
                    CloseButtonClickY,
                    cancellationToken).ConfigureAwait(false);
                if (!clicked)
                {
                    PauseForSafety("The Chest Hunt result screen could not be closed safely.");
                    return true;
                }

                _clickExecuted();

                _log.Info(perfectChestOpened
                    ? "Perfect Chest Hunt completed and the result screen was closed."
                    : "Chest Hunt completed and the result screen was closed.");
                return true;
            }

            if (calibration.IsPerfectChestVisible(frame))
            {
                bool clicked = await _pointerAdapter.ClickClientPointAsync(
                    PerfectChestClickX,
                    PerfectChestClickY,
                    cancellationToken).ConfigureAwait(false);
                if (!clicked)
                {
                    PauseForSafety("The Perfect Chest reward could not be opened safely.");
                    return true;
                }

                _clickExecuted();

                perfectChestOpened = true;
            }

            await Task.Delay(OutcomePollInterval, cancellationToken).ConfigureAwait(false);
        }

        PauseForSafety("The Chest Hunt end screen was not recognized; automation was paused for safety.");
        return true;
    }

    private async Task<bool> CanClickAsync(
        AutomationSettings settings,
        CancellationToken cancellationToken)
    {
        if (_isPaused())
        {
            return false;
        }

        GameSnapshot snapshot = await _gameAdapter
            .GetSnapshotAsync(cancellationToken)
            .ConfigureAwait(false);
        return snapshot.CanReceiveInput(settings);
    }

    private async Task<bool> DelayWhileRunningAsync(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + delay;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (_isPaused())
            {
                return false;
            }

            TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            await Task.Delay(
                remaining < OutcomePollInterval ? remaining : OutcomePollInterval,
                cancellationToken).ConfigureAwait(false);
        }

        return !_isPaused();
    }

    private void PauseForSafety(string message)
    {
        _log.Info(message);
        _pauseAutomation();
    }
}
