using IdleRunner.Core;

namespace IdleRunner.Desktop;

public sealed class BonusStageAutomationService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan PixelPollInterval = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan DefaultPixelTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SliderTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SkipTimeout = TimeSpan.FromMinutes(4);

    private readonly IGameAdapter _gameAdapter;
    private readonly IGamePointerAdapter _pointerAdapter;
    private readonly IGameControlAdapter _controlAdapter;
    private readonly IGameFrameSource _frameSource;
    private readonly IAutomationLog _log;
    private readonly Func<AutomationSettings> _settingsProvider;
    private readonly Func<bool> _isPaused;
    private readonly Action _inputExecuted;
    private readonly AutomationActivityCoordinator _coordinator;
    private bool _waitingForExit;

    public BonusStageAutomationService(
        IGameAdapter gameAdapter,
        IGamePointerAdapter pointerAdapter,
        IGameControlAdapter controlAdapter,
        IGameFrameSource frameSource,
        IAutomationLog log,
        Func<AutomationSettings> settingsProvider,
        Func<bool> isPaused,
        Action inputExecuted,
        AutomationActivityCoordinator coordinator)
    {
        _gameAdapter = gameAdapter ?? throw new ArgumentNullException(nameof(gameAdapter));
        _pointerAdapter = pointerAdapter ?? throw new ArgumentNullException(nameof(pointerAdapter));
        _controlAdapter = controlAdapter ?? throw new ArgumentNullException(nameof(controlAdapter));
        _frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        _isPaused = isPaused ?? throw new ArgumentNullException(nameof(isPaused));
        _inputExecuted = inputExecuted ?? throw new ArgumentNullException(nameof(inputExecuted));
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
                _log.Error("The Bonus Stage automation cycle failed.", exception);
                _log.Info("Bonus Stage execution ended after an unexpected error; the main automation remains running.");
                _waitingForExit = true;
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
            _waitingForExit = false;
            return;
        }

        if (_coordinator.IsExclusiveActionActive)
        {
            return;
        }

        AutomationSettings settings = _settingsProvider().Normalize();
        GameSnapshot snapshot = await _gameAdapter.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshot.CanReceiveInput(settings))
        {
            return;
        }

        GameFrame? frame = await CaptureAsync(cancellationToken).ConfigureAwait(false);
        bool entranceVisible = frame is not null && BonusStageVisualDetector.IsEntranceVisible(frame);
        if (!entranceVisible)
        {
            _waitingForExit = false;
            return;
        }

        if (_waitingForExit)
        {
            return;
        }

        if (!_coordinator.TryEnter())
        {
            return;
        }

        _waitingForExit = true;
        try
        {
            await PlayAsync(frame!, settings.SkipBonusStage, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _coordinator.Exit();
        }
    }

    private async Task PlayAsync(
        GameFrame entranceFrame,
        bool skipBonusStage,
        CancellationToken cancellationToken)
    {
        _log.Info("Bonus Stage detected. Legacy route execution started.");
        if (!await DelayWhileRunningAsync(200, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        GameFrame? stageFrame = await CaptureAsync(cancellationToken).ConfigureAwait(false);
        bool stage3 = BonusStageVisualDetector.IsStage3(stageFrame ?? entranceFrame);
        if (!await ClearSliderAsync(cancellationToken).ConfigureAwait(false))
        {
            _log.Info("The Bonus Stage opening slider was not detected; continuing with the route.");
        }

        int stageNumber = stage3 ? 3 : 2;
        if (skipBonusStage)
        {
            _log.Info($"Skip Bonus Stage is enabled. Waiting for Bonus Stage {stageNumber} to end.");
            if (!await WaitForFailureAndCloseAsync(stageNumber, SkipTimeout, cancellationToken).ConfigureAwait(false))
            {
                _log.Info("The skipped Bonus Stage result was not detected; the main automation remains running.");
            }

            return;
        }

        if (!await DelayWhileRunningAsync(3900, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        GameFrame? routeFrame = await CaptureAsync(cancellationToken).ConfigureAwait(false);
        bool spiritBoost = routeFrame is not null && BonusStageVisualDetector.HasSpiritBoost(routeFrame);
        _log.Info($"Running Bonus Stage {stageNumber}{(spiritBoost ? " Spirit Boost" : string.Empty)} route.");

        bool completed = stage3
            ? await PlayStage3Async(spiritBoost, cancellationToken).ConfigureAwait(false)
            : await PlayStage2Async(spiritBoost, cancellationToken).ConfigureAwait(false);

        if (completed)
        {
            _log.Info($"Bonus Stage {stageNumber} route completed.");
        }
    }

    private async Task<bool> ClearSliderAsync(CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow + SliderTimeout;
        do
        {
            GameFrame? frame = await CaptureAsync(cancellationToken).ConfigureAwait(false);
            if (frame is null)
            {
                return false;
            }

            await MoveVisibleSliderAsync(frame, cancellationToken).ConfigureAwait(false);
            if (!await DelayWhileRunningAsync(500, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            frame = await CaptureAsync(cancellationToken).ConfigureAwait(false);
            if (frame is not null && !BonusStageVisualDetector.IsSliderVisible(frame))
            {
                return true;
            }
        }
        while (DateTime.UtcNow < deadline && !_isPaused());

        return false;
    }

    private async Task MoveVisibleSliderAsync(GameFrame frame, CancellationToken cancellationToken)
    {
        bool topLeft = BonusStageVisualDetector.ContainsColor(frame, 441, 560, 443, 560, 0x00, 0x7E, 0x00);
        bool bottomLeft = BonusStageVisualDetector.ContainsColor(frame, 441, 620, 443, 620, 0x00, 0x7E, 0x00);
        bool topRight = BonusStageVisualDetector.ContainsColor(frame, 847, 560, 850, 560, 0x00, 0x7E, 0x00);
        bool bottomRight = BonusStageVisualDetector.ContainsColor(frame, 847, 620, 850, 620, 0x00, 0x7E, 0x00);

        if (topLeft)
        {
            await DragAsync(840, 560, 450, 560, cancellationToken).ConfigureAwait(false);
        }
        else if (bottomLeft)
        {
            await DragAsync(840, 620, 450, 620, cancellationToken).ConfigureAwait(false);
        }
        else if (topRight)
        {
            await DragAsync(450, 560, 840, 560, cancellationToken).ConfigureAwait(false);
        }
        else if (bottomRight)
        {
            await DragAsync(450, 620, 840, 620, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> PlayStage2Async(bool spiritBoost, CancellationToken cancellationToken)
    {
        (int Left, int Top, int Right, int Bottom, byte R, byte G, byte B, int Delay)[] syncPoints =
        [
            (220, 465, 220, 465, 0xCF, 0xBC, 0xB8, 200),
            (780, 513, 780, 513, 0xBB, 0x26, 0xDF, 0),
            (151, 465, 220, 465, 0xCF, 0xBC, 0xB8, 0),
            (100, 250, 250, 472, 0x0D, 0x20, 0x30, 200)
        ];

        string routeName = spiritBoost ? "BonusStage2SB" : "BonusStage2";
        for (int section = 1; section <= 4; section++)
        {
            var sync = syncPoints[section - 1];
            await WaitForColorAsync(
                sync.Left, sync.Top, sync.Right, sync.Bottom,
                sync.R, sync.G, sync.B,
                DefaultPixelTimeout,
                cancellationToken).ConfigureAwait(false);
            if (!await DelayWhileRunningAsync(sync.Delay, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            BonusStage2SectionPlan plan = BonusStage2Routes.GetSection(spiritBoost, section);
            if (!await ExecuteAsync(plan.Traversal, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            if (await CheckStage2FailureAsync(cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            if (plan.CollectionLeadIn is BonusStageJumpCommand leadIn
                && !await JumpAsync(leadIn, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            if (!await CollectAsync(plan.CollectionJumpCount, cancellationToken).ConfigureAwait(false)
                || await CheckStage2FailureAsync(cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            _log.Info($"{routeName} Section {section} complete.");
        }

        return true;
    }

    private async Task<bool> PlayStage3Async(bool spiritBoost, CancellationToken cancellationToken)
    {
        for (int section = 1; section <= 4; section++)
        {
            bool completed = false;
            for (int attempt = 1; attempt <= 2 && !completed; attempt++)
            {
                completed = section switch
                {
                    1 => await PlayStage3Section1Async(spiritBoost, cancellationToken).ConfigureAwait(false),
                    2 => await PlayStage3Section2Async(spiritBoost, cancellationToken).ConfigureAwait(false),
                    3 => await PlayStage3Section3Async(spiritBoost, cancellationToken).ConfigureAwait(false),
                    4 => await PlayStage3Section4Async(spiritBoost, cancellationToken).ConfigureAwait(false),
                    _ => false
                };

                if (!completed && attempt == 1)
                {
                    _log.Info($"BonusStage3{(spiritBoost ? "SB" : string.Empty)} Section {section} retry.");
                }
            }

            if (!completed)
            {
                return false;
            }

            _log.Info($"BonusStage3{(spiritBoost ? "SB" : string.Empty)} Section {section} complete.");
        }

        return true;
    }

    private async Task<bool> PlayStage3Section1Async(bool spiritBoost, CancellationToken cancellationToken)
    {
        await WaitForWhiteAsync(520, 200, 580, 250, null, cancellationToken).ConfigureAwait(false);
        if (!await DelayWhileRunningAsync(360, cancellationToken).ConfigureAwait(false)
            || !await ExecuteAsync(Commands((140, 530), (70, 640), (80, 740), (150, 765), (65, 625), (65, 500)), cancellationToken).ConfigureAwait(false)) return false;
        await WaitForWhiteAsync(540, 335, 555, 360, 700, cancellationToken).ConfigureAwait(false);
        if (!await ExecuteAsync(Commands((150, 750), (200, 200)), cancellationToken).ConfigureAwait(false)) return false;
        await WaitForWhiteAsync(390, 230, 410, 250, 3500, cancellationToken).ConfigureAwait(false);
        if (!await ExecuteAsync(Commands((130, 545), (70, 620), (80, 400)), cancellationToken).ConfigureAwait(false)) return false;
        await WaitForWhiteAsync(500, 200, 515, 250, 1500, cancellationToken).ConfigureAwait(false);
        if (!await ExecuteAsync(Commands((150, 755), (65, 625), (65, 500)), cancellationToken).ConfigureAwait(false)) return false;
        await WaitForWhiteAsync(540, 335, 555, 360, 700, cancellationToken).ConfigureAwait(false);
        if (!await ExecuteAsync(Commands(
            (150, 740), (200, 1490), (110, 580), (65, 640), (80, 740),
            (150, 765), (130, 560), (70, 650)), cancellationToken).ConfigureAwait(false)) return false;
        return await DelayWhileRunningAsync(800, cancellationToken).ConfigureAwait(false)
            && await CollectStage3Async(spiritBoost, 21, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> PlayStage3Section2Async(bool spiritBoost, CancellationToken cancellationToken)
    {
        await WaitForWhiteAsync(306, 200, 309, 275, null, cancellationToken).ConfigureAwait(false);
        for (int iteration = 0; iteration < 2; iteration++)
        {
            if (!await ExecuteAsync(Commands((80, 440), (95, 660), (105, 500)), cancellationToken).ConfigureAwait(false)) return false;
            await WaitForWhiteAsync(475, 445, 482, 475, 1000, cancellationToken).ConfigureAwait(false);
            if (!await ExecuteAsync(Commands((79, 601), (63, 980), (82, 440), (95, 660), (48, 750)), cancellationToken).ConfigureAwait(false)) return false;
            await WaitForWhiteAsync(515, 265, 522, 285, 1200, cancellationToken).ConfigureAwait(false);
            if (!await JumpAsync(new(63, 500), cancellationToken).ConfigureAwait(false)
                || !await WallJumpAsync(1, 50, cancellationToken).ConfigureAwait(false)
                || !await DelayWhileRunningAsync(80, cancellationToken).ConfigureAwait(false)
                || !await WallJumpAsync(1, 50, cancellationToken).ConfigureAwait(false)
                || !await DelayWhileRunningAsync(300, cancellationToken).ConfigureAwait(false)
                || !await WallJumpAsync(4, 50, cancellationToken).ConfigureAwait(false)) return false;
            await WaitForWhiteAsync(306, 200, 309, 275, 1000, cancellationToken).ConfigureAwait(false);
        }

        return await ExecuteAsync(Commands((80, 440), (95, 660)), cancellationToken).ConfigureAwait(false)
            && await CollectStage3Async(spiritBoost, 25, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> PlayStage3Section3Async(bool spiritBoost, CancellationToken cancellationToken)
    {
        bool upperWay = false;
        await WaitForWhiteAsync(280, 385, 330, 435, null, cancellationToken).ConfigureAwait(false);
        if (!await DelayWhileRunningAsync(600, cancellationToken).ConfigureAwait(false)) return false;

        for (int iteration = 0; iteration < 2; iteration++)
        {
            if (upperWay)
            {
                if (!await WallJumpAsync(5, 50, cancellationToken).ConfigureAwait(false)) return false;
                upperWay = false;
            }
            else
            {
                if (!await JumpAsync(new(120, 100), cancellationToken).ConfigureAwait(false)) return false;
                await WaitForWhiteAsync(205, 395, 215, 410, 780, cancellationToken).ConfigureAwait(false);
                if (!await JumpAsync(new(95, 225), cancellationToken).ConfigureAwait(false)
                    || !await WallJumpAsync(5, 50, cancellationToken).ConfigureAwait(false)) return false;
            }

            if (!await DelayWhileRunningAsync(2000, cancellationToken).ConfigureAwait(false)
                || !await JumpAsync(new(300, 500), cancellationToken).ConfigureAwait(false)
                || !await WallJumpAsync(5, 50, cancellationToken).ConfigureAwait(false)) return false;
            await WaitForWhiteAsync(205, 395, 215, 410, 900, cancellationToken).ConfigureAwait(false);
            if (!await JumpAsync(new(95, 250), cancellationToken).ConfigureAwait(false)
                || !await WallJumpAsync(1, 715, cancellationToken).ConfigureAwait(false)
                || !await WallJumpAsync(6, 50, cancellationToken).ConfigureAwait(false)) return false;

            bool found = await WaitForWhiteAsync(
                300, 415, 330, 435,
                spiritBoost ? 1000 : 2500,
                cancellationToken).ConfigureAwait(false);
            if (!found)
            {
                upperWay = true;
            }
            else if (!await DelayWhileRunningAsync(2500, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            if (!await DelayWhileRunningAsync(150, cancellationToken).ConfigureAwait(false)) return false;
        }

        return await CollectStage3Async(spiritBoost, 25, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> PlayStage3Section4Async(bool spiritBoost, CancellationToken cancellationToken)
    {
        await WaitForWhiteAsync(330, 170, 380, 195, null, cancellationToken).ConfigureAwait(false);
        if (spiritBoost)
        {
            for (int i = 0; i < 14; i++)
            {
                if (!await JumpAsync(new(110, 1180), cancellationToken).ConfigureAwait(false)) return false;
            }

            if (!await JumpAsync(new(300, 300), cancellationToken).ConfigureAwait(false)) return false;
        }
        else
        {
            IReadOnlyList<BonusStageJumpCommand> sequence = Commands(
                (300, 600), (300, 420), (35, 748), (35, 540), (35, 1160));
            for (int i = 0; i < 5; i++)
            {
                if (!await ExecuteAsync(sequence, cancellationToken).ConfigureAwait(false)) return false;
            }
        }

        return await CollectStage3Async(spiritBoost, 25, false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> CollectStage3Async(
        bool spiritBoost,
        int count,
        bool stopEarly,
        CancellationToken cancellationToken)
    {
        if (await CheckStage3FailureAsync(spiritBoost, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        for (int i = 1; i <= count; i++)
        {
            if (stopEarly && i > 8)
            {
                bool nextSection = await WaitForColorAsync(
                    1100, 240, 1100, 440,
                    0x8D, 0x87, 0xA2,
                    TimeSpan.FromMilliseconds(480),
                    cancellationToken).ConfigureAwait(false);
                if (nextSection)
                {
                    break;
                }
            }
            else if (!await DelayWhileRunningAsync(500, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            if (!await JumpAsync(new(0, 0), cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }

        return !await CheckStage3FailureAsync(spiritBoost, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> CheckStage2FailureAsync(CancellationToken cancellationToken)
    {
        GameFrame? frame = await CaptureAsync(cancellationToken).ConfigureAwait(false);
        if (frame is null || !BonusStageVisualDetector.IsFailureVisible(frame))
        {
            return false;
        }

        await ClickAsync(716, 600, cancellationToken).ConfigureAwait(false);
        _log.Info("BonusStage2 failed.");
        return true;
    }

    private async Task<bool> CheckStage3FailureAsync(bool spiritBoost, CancellationToken cancellationToken)
    {
        GameFrame? frame = await CaptureAsync(cancellationToken).ConfigureAwait(false);
        if (frame is null || !BonusStageVisualDetector.IsStage3FailureVisible(frame))
        {
            return false;
        }

        string routeName = spiritBoost ? "BonusStage3SB" : "BonusStage3";
        if (BonusStageVisualDetector.IsRetryVisible(frame))
        {
            await ClickAsync(560, 600, cancellationToken).ConfigureAwait(false);
            _log.Info($"{routeName} retry selected.");
            await DelayWhileRunningAsync(1000, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _log.Info($"{routeName} failed.");
        }

        return true;
    }

    private async Task<bool> WaitForFailureAndCloseAsync(
        int stageNumber,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !_isPaused())
        {
            GameFrame? frame = await CaptureAsync(cancellationToken).ConfigureAwait(false);
            if (frame is not null && BonusStageVisualDetector.IsFailureVisible(frame))
            {
                if (await ClickAsync(716, 600, cancellationToken).ConfigureAwait(false))
                {
                    _log.Info($"Bonus Stage {stageNumber} skipped and its result screen was closed.");
                    return true;
                }
            }

            if (!await DelayWhileRunningAsync(200, cancellationToken).ConfigureAwait(false)) return false;
        }

        return false;
    }

    private async Task<bool> CollectAsync(int count, CancellationToken cancellationToken)
    {
        for (int i = 0; i < count; i++)
        {
            if (!await JumpAsync(new(0, 500), cancellationToken).ConfigureAwait(false)) return false;
        }

        return true;
    }

    private async Task<bool> WallJumpAsync(
        int count,
        int waitMilliseconds,
        CancellationToken cancellationToken)
    {
        for (int i = 0; i < count; i++)
        {
            if (!await JumpAsync(new(30, waitMilliseconds), cancellationToken).ConfigureAwait(false)) return false;
        }

        return true;
    }

    private async Task<bool> ExecuteAsync(
        IReadOnlyList<BonusStageJumpCommand> commands,
        CancellationToken cancellationToken)
    {
        foreach (BonusStageJumpCommand command in commands)
        {
            if (!await JumpAsync(command, cancellationToken).ConfigureAwait(false)) return false;
        }

        return true;
    }

    private async Task<bool> JumpAsync(
        BonusStageJumpCommand command,
        CancellationToken cancellationToken)
    {
        if (!await WaitUntilReadyAsync(cancellationToken).ConfigureAwait(false)) return false;
        bool sent = await _controlAdapter
            .HoldJumpAsync(command.HoldMilliseconds, cancellationToken)
            .ConfigureAwait(false);
        if (!sent)
        {
            _log.Info("A Bonus Stage jump could not be delivered; waiting for the next detected stage.");
            return false;
        }

        _inputExecuted();
        return await DelayWhileRunningAsync(command.WaitMilliseconds, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> DragAsync(
        int startX,
        int startY,
        int endX,
        int endY,
        CancellationToken cancellationToken)
    {
        if (!await WaitUntilReadyAsync(cancellationToken).ConfigureAwait(false)) return false;
        bool sent = await _controlAdapter
            .DragClientPointAsync(startX, startY, endX, endY, cancellationToken)
            .ConfigureAwait(false);
        if (sent) _inputExecuted();
        return sent;
    }

    private async Task<bool> ClickAsync(int x, int y, CancellationToken cancellationToken)
    {
        if (!await WaitUntilReadyAsync(cancellationToken).ConfigureAwait(false)) return false;
        bool sent = await _pointerAdapter.ClickClientPointAsync(x, y, cancellationToken).ConfigureAwait(false);
        if (sent) _inputExecuted();
        return sent;
    }

    private Task<bool> WaitForWhiteAsync(
        int left,
        int top,
        int right,
        int bottom,
        int? timeoutMilliseconds,
        CancellationToken cancellationToken) =>
        WaitForColorAsync(
            left, top, right, bottom,
            0xFF, 0xFF, 0xFF,
            timeoutMilliseconds is null
                ? DefaultPixelTimeout
                : TimeSpan.FromMilliseconds(timeoutMilliseconds.Value),
            cancellationToken);

    private async Task<bool> WaitForColorAsync(
        int left,
        int top,
        int right,
        int bottom,
        byte red,
        byte green,
        byte blue,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !_isPaused())
        {
            GameFrame? frame = await CaptureAsync(cancellationToken).ConfigureAwait(false);
            if (frame is not null
                && BonusStageVisualDetector.ContainsColor(
                    frame, left, top, right, bottom, red, green, blue))
            {
                return true;
            }

            await Task.Delay(PixelPollInterval, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private Task<GameFrame?> CaptureAsync(CancellationToken cancellationToken) =>
        _frameSource.CaptureAsync(cancellationToken);

    private async Task<bool> DelayWhileRunningAsync(
        int milliseconds,
        CancellationToken cancellationToken)
    {
        int remaining = Math.Max(0, milliseconds);
        while (remaining > 0)
        {
            if (_isPaused()) return false;
            AutomationSettings settings = _settingsProvider().Normalize();
            GameSnapshot snapshot = await _gameAdapter.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (!snapshot.CanReceiveInput(settings))
            {
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                continue;
            }

            int slice = Math.Min(100, remaining);
            await Task.Delay(slice, cancellationToken).ConfigureAwait(false);
            remaining -= slice;
        }

        return !_isPaused();
    }

    private async Task<bool> WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        while (!_isPaused())
        {
            AutomationSettings settings = _settingsProvider().Normalize();
            GameSnapshot snapshot = await _gameAdapter.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (snapshot.CanReceiveInput(settings))
            {
                return true;
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private static IReadOnlyList<BonusStageJumpCommand> Commands(
        params (int Hold, int Wait)[] commands) =>
        commands.Select(command => new BonusStageJumpCommand(command.Hold, command.Wait)).ToArray();
}
