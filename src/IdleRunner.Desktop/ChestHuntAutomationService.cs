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
    private const int CloseButtonClickY = 658;
    private const int MaximumChestClickAttempts = 3;

    private readonly IGameAdapter _gameAdapter;
    private readonly IGamePointerAdapter _pointerAdapter;
    private readonly IGameFrameSource _frameSource;
    private readonly ChestHuntVisualService _visualService;
    private readonly IAutomationLog _log;
    private readonly Func<AutomationSettings> _settingsProvider;
    private readonly Func<bool> _isPaused;
    private readonly Action _clickExecuted;
    private readonly AutomationActivityCoordinator _coordinator;
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
        Action clickExecuted,
        AutomationActivityCoordinator coordinator)
    {
        _gameAdapter = gameAdapter ?? throw new ArgumentNullException(nameof(gameAdapter));
        _pointerAdapter = pointerAdapter ?? throw new ArgumentNullException(nameof(pointerAdapter));
        _frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
        _visualService = visualService ?? throw new ArgumentNullException(nameof(visualService));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        _isPaused = isPaused ?? throw new ArgumentNullException(nameof(isPaused));
        _clickExecuted = clickExecuted ?? throw new ArgumentNullException(nameof(clickExecuted));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
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
                ReportIssue("Chest Hunt execution ended after an unexpected error; the main automation remains running.");
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
        if (_isPaused())
        {
            _waitingForGridExit = false;
            return;
        }

        if (_coordinator.IsExclusiveActionActive)
        {
            return;
        }

        if (!_visualService.HasCalibration)
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

        ChestHuntCalibration? calibration = _visualService.Calibration;
        GameFrame? currentFrame = await _frameSource
            .CaptureAsync(cancellationToken)
            .ConfigureAwait(false);
        if (calibration is not null
            && currentFrame is not null
            && calibration.IsHuntEndVisible(currentFrame))
        {
            bool closed = await _pointerAdapter.ClickClientPointAsync(
                CloseButtonClickX,
                CloseButtonClickY,
                cancellationToken).ConfigureAwait(false);
            if (closed)
            {
                _clickExecuted();
                _log.Info("Visible Chest Hunt result screen was closed.");
                _waitingForGridExit = true;
            }

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

        if (!observation.IsFresh && !_reportedUnsafeResume)
        {
            _reportedUnsafeResume = true;
            _log.Info(
                $"Chest Hunt detected with {observation.OpenedNonSaverCount} chest(s) already open. "
                + "Continuing from the remaining grid with the legacy no-Mimic fallback state.");
        }

        if (observation.IsFresh)
        {
            _reportedUnsafeResume = false;
            _log.Info(
                $"Fresh Chest Hunt detected. Saver is at row {observation.Saver.Position.Row}, "
                + $"column {observation.Saver.Position.Column}.");
        }

        if (!_coordinator.TryEnter())
        {
            return;
        }

        try
        {
            bool completed = await PlayAsync(
                observation.Saver.Position,
                settings,
                cancellationToken).ConfigureAwait(false);
            _waitingForGridExit = completed;
        }
        finally
        {
            _coordinator.Exit();
        }
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
            || !readyObservation.IsActive
            || readyObservation.Saver.Position != saverPosition)
        {
            ReportIssue("Chest Hunt changed before execution started; this hunt was skipped.");
            return true;
        }

        ChestHuntGridPosition[] alreadyOpened = ChestHuntCalibration.Positions
            .Where(position => position != saverPosition)
            .Where(position => !readyObservation.ClosedPositions.Contains(position))
            .ToArray();
        bool openLifeSaverFirst = alreadyOpened.Length > 0
            && (settings.NoReinforcedCrystalSaver
                || (!settings.PerfectChestHunt && alreadyOpened.Length >= 2));
        ChestHuntSession session = new(
            saverPosition,
            alreadyOpened,
            openLifeSaverFirst);

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
            if (beforeClick is not null && calibration.IsHuntEndVisible(beforeClick))
            {
                return await CloseResultScreenAsync(calibration, cancellationToken)
                    .ConfigureAwait(false);
            }

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
                ReportIssue(
                    $"Chest Hunt target at row {nextPosition.Value.Row}, "
                    + $"column {nextPosition.Value.Column} was not in the expected state; "
                    + "this hunt was skipped before clicking.");
                return true;
            }

            ChestHuntOutcome outcome = ChestHuntOutcome.Unknown;
            GameFrame? frame = null;

            for (int attempt = 1; attempt <= MaximumChestClickAttempts; attempt++)
            {
                if (!await CanClickAsync(settings, cancellationToken).ConfigureAwait(false)
                    || !await _pointerAdapter.ClickClientPointAsync(
                        nextPosition.Value.CenterX,
                        nextPosition.Value.CenterY,
                        cancellationToken).ConfigureAwait(false))
                {
                    ReportIssue("Chest Hunt click was rejected because the game is not ready for input.");
                    return true;
                }

                _clickExecuted();

                if (!await DelayWhileRunningAsync(timing.ChestRevealDelay, cancellationToken)
                        .ConfigureAwait(false))
                {
                    return false;
                }

                frame = await _frameSource.CaptureAsync(cancellationToken).ConfigureAwait(false);
                if (frame is null)
                {
                    ReportIssue("Chest Hunt result could not be captured; this hunt was skipped.");
                    return true;
                }

                outcome = session.IsOpeningLifeSaver
                    ? calibration.IsSaverVisible(frame)
                        ? ChestHuntOutcome.Unknown
                        : ChestHuntOutcome.LifeSaver
                    : calibration.DetectOutcome(frame, nextPosition.Value);

                if (outcome != ChestHuntOutcome.Unknown)
                {
                    break;
                }

                if (attempt < MaximumChestClickAttempts)
                {
                    _log.Info(
                        $"Chest Hunt row {nextPosition.Value.Row}, column {nextPosition.Value.Column} "
                        + $"did not react; retrying click {attempt + 1}/{MaximumChestClickAttempts}.");
                }
            }

            if (outcome == ChestHuntOutcome.Unknown)
            {
                outcome = session.IsOpeningLifeSaver
                    ? ChestHuntOutcome.LifeSaver
                    : ChestHuntOutcome.Reward;
                _log.Info(
                    $"Chest Hunt row {nextPosition.Value.Row}, column {nextPosition.Value.Column} "
                    + $"has no special result signature; continuing as {outcome} using the legacy fallback.");
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
                    ReportIssue("The Mimic result could not be verified; this hunt was skipped.");
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
                    ReportIssue("The Chest Hunt result screen could not be closed.");
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
                    ReportIssue("The Perfect Chest reward could not be opened.");
                    return true;
                }

                _clickExecuted();

                perfectChestOpened = true;
            }

            await Task.Delay(OutcomePollInterval, cancellationToken).ConfigureAwait(false);
        }

        ReportIssue("The Chest Hunt end screen was not recognized; the main automation remains running.");
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
        TimeSpan remaining = delay;
        while (remaining > TimeSpan.Zero)
        {
            if (_isPaused())
            {
                return false;
            }

            AutomationSettings settings = _settingsProvider().Normalize();
            GameSnapshot snapshot = await _gameAdapter
                .GetSnapshotAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!snapshot.CanReceiveInput(settings))
            {
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                continue;
            }

            TimeSpan slice = remaining < OutcomePollInterval ? remaining : OutcomePollInterval;
            await Task.Delay(slice, cancellationToken).ConfigureAwait(false);
            remaining -= slice;
        }

        return !_isPaused();
    }

    private void ReportIssue(string message)
    {
        _log.Info(message);
    }
}
