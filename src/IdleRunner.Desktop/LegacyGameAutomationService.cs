using System.Diagnostics;
using IdleRunner.Core;

namespace IdleRunner.Desktop;

public sealed class LegacyGameAutomationService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
    private readonly IGameAdapter _gameAdapter;
    private readonly IGamePointerAdapter _pointerAdapter;
    private readonly IGameControlAdapter _controlAdapter;
    private readonly IGameFrameSource _frameSource;
    private readonly IAutomationLog _log;
    private readonly Func<AutomationSettings> _settingsProvider;
    private readonly Func<VisualObservationDefinition?> _silverBoxObservationProvider;
    private readonly Func<bool> _isStopped;
    private readonly AutomationActivityCoordinator _coordinator;
    private readonly Action _inputExecuted;
    private readonly Action _settingsChanged;
    private long _lastFiveSecondCheck;
    private DateTime _nextAutoBuyUtc;
    private DateTime _nextAutoAscendUtc;
    private DateTime _nextSoulBonusRageUtc;
    private bool _wasAutoBuyEnabled;
    private bool _wasAutoAscendEnabled;

    public LegacyGameAutomationService(
        IGameAdapter gameAdapter,
        IGamePointerAdapter pointerAdapter,
        IGameControlAdapter controlAdapter,
        IGameFrameSource frameSource,
        IAutomationLog log,
        Func<AutomationSettings> settingsProvider,
        Func<VisualObservationDefinition?> silverBoxObservationProvider,
        Func<bool> isStopped,
        AutomationActivityCoordinator coordinator,
        Action inputExecuted,
        Action settingsChanged)
    {
        _gameAdapter = gameAdapter ?? throw new ArgumentNullException(nameof(gameAdapter));
        _pointerAdapter = pointerAdapter ?? throw new ArgumentNullException(nameof(pointerAdapter));
        _controlAdapter = controlAdapter ?? throw new ArgumentNullException(nameof(controlAdapter));
        _frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        _silverBoxObservationProvider = silverBoxObservationProvider
            ?? throw new ArgumentNullException(nameof(silverBoxObservationProvider));
        _isStopped = isStopped ?? throw new ArgumentNullException(nameof(isStopped));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _inputExecuted = inputExecuted ?? throw new ArgumentNullException(nameof(inputExecuted));
        _settingsChanged = settingsChanged ?? throw new ArgumentNullException(nameof(settingsChanged));
        _lastFiveSecondCheck = Stopwatch.GetTimestamp();
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
                _log.Error("A legacy automation module ended unexpectedly; automation remains running.", exception);
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task RunAutoUpgradeNowAsync(CancellationToken cancellationToken)
    {
        if (_isStopped())
        {
            _log.Info("Start automation before requesting a manual auto upgrade.");
            return Task.CompletedTask;
        }

        return RunExclusiveAsync(AutoUpgradeAsync, cancellationToken);
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (_isStopped() || _coordinator.IsExclusiveActionActive)
        {
            return;
        }

        AutomationSettings settings = _settingsProvider().Normalize();
        GameSnapshot snapshot = await _gameAdapter.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshot.CanReceiveInput(settings))
        {
            return;
        }

        GameFrame? frame = await _frameSource.CaptureAsync(cancellationToken).ConfigureAwait(false);
        if (frame is null)
        {
            return;
        }

        if (BonusStageVisualDetector.IsEntranceVisible(frame)
            || IsChestHuntEntrance(frame))
        {
            return;
        }

        VisualObservationDefinition? silverObservation = _silverBoxObservationProvider();
        bool silverBoxVisible = HasColor(
            frame, 642, 28, 658, 44, 0xCA, 0x97, 0x00, 24)
            || (silverObservation is not null && silverObservation.Evaluate(frame).IsMatch);
        if (settings.CollectSilverBoxesEnabled && silverBoxVisible)
        {
            if (await ClickAsync(
                SilverBoxCalibration.ClickX,
                SilverBoxCalibration.ClickY,
                cancellationToken).ConfigureAwait(false))
            {
                _log.Info("Silver Box collected.");
            }

            return;
        }

        if (HasColor(frame, 382, 277, 388, 283, 0x14, 0x0C, 0x1C, 12))
        {
            await RunExclusiveAsync(RageWhenHordeAsync, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (HasColor(frame, 1127, 607, 1133, 613, 0xCB, 0xCB, 0x4C, 12))
        {
            await RunExclusiveAsync(ClaimQuestsAsync, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (HasSoulBonus(frame) && DateTime.UtcNow >= _nextSoulBonusRageUtc)
        {
            _nextSoulBonusRageUtc = DateTime.UtcNow.AddSeconds(2);
            await PressRageAsync(cancellationToken).ConfigureAwait(false);
        }

        if (HasColor(frame, 96, 110, 102, 116, 0xFF, 0xFF, 0x7A, 12))
        {
            await RunExclusiveAsync(CollectMinionsAsync, cancellationToken).ConfigureAwait(false);
            return;
        }

        long now = Stopwatch.GetTimestamp();
        if ((now - _lastFiveSecondCheck) / (double)Stopwatch.Frequency < 5)
        {
            return;
        }

        _lastFiveSecondCheck = now;
        await CloseVisibleResultAsync(frame, cancellationToken).ConfigureAwait(false);

        if (IsBossEntrance(frame))
        {
            await RunExclusiveAsync(
                token => IsKnightBoss(frame) ? PlayKnightAsync(token) : PlayVictorAsync(token),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (IsAscendingHeightsEntrance(frame))
        {
            await RunExclusiveAsync(PlayAscendingHeightsAsync, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (settings.CirclePortalsEnabled && IsPortalReady(frame))
        {
            await RunExclusiveAsync(CirclePortalsAsync, cancellationToken).ConfigureAwait(false);
            return;
        }

        ScheduleTimers(settings);
        if (settings.AutoBuyUpgradesEnabled && DateTime.UtcNow >= _nextAutoBuyUtc)
        {
            _nextAutoBuyUtc = DateTime.UtcNow.AddMinutes(settings.AutoBuyIntervalMinutes);
            await RunExclusiveAsync(AutoUpgradeAsync, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (settings.AutoAscendEnabled && DateTime.UtcNow >= _nextAutoAscendUtc)
        {
            _nextAutoAscendUtc = DateTime.UtcNow.AddMinutes(settings.AutoAscendIntervalMinutes);
            await RunExclusiveAsync(AutoAscendAsync, cancellationToken).ConfigureAwait(false);
        }
    }

    private void ScheduleTimers(AutomationSettings settings)
    {
        if (settings.AutoBuyUpgradesEnabled && !_wasAutoBuyEnabled)
        {
            _nextAutoBuyUtc = DateTime.UtcNow.AddSeconds(9);
        }
        else if (!settings.AutoBuyUpgradesEnabled)
        {
            _nextAutoBuyUtc = default;
        }

        if (settings.AutoAscendEnabled && !_wasAutoAscendEnabled)
        {
            _nextAutoAscendUtc = DateTime.UtcNow.AddMinutes(settings.AutoAscendIntervalMinutes);
        }
        else if (!settings.AutoAscendEnabled)
        {
            _nextAutoAscendUtc = default;
        }

        _wasAutoBuyEnabled = settings.AutoBuyUpgradesEnabled;
        _wasAutoAscendEnabled = settings.AutoAscendEnabled;

        if (settings.AutoBuyUpgradesEnabled && _nextAutoBuyUtc == default)
        {
            _nextAutoBuyUtc = DateTime.UtcNow.AddMinutes(settings.AutoBuyIntervalMinutes);
        }

        if (settings.AutoAscendEnabled && _nextAutoAscendUtc == default)
        {
            _nextAutoAscendUtc = DateTime.UtcNow.AddMinutes(settings.AutoAscendIntervalMinutes);
        }
    }

    private async Task RunExclusiveAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        if (!_coordinator.TryEnter())
        {
            return;
        }

        try
        {
            await action(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _coordinator.Exit();
        }
    }

    private async Task AutoUpgradeAsync(CancellationToken cancellationToken)
    {
        _log.Info("Auto upgrade started.");
        await ClickAsync(1244, 712, cancellationToken).ConfigureAwait(false);
        await DelayAsync(150, cancellationToken).ConfigureAwait(false);
        await ClickAsync(1163, 655, cancellationToken).ConfigureAwait(false);
        await DelayAsync(250, cancellationToken).ConfigureAwait(false);

        int cycles = 0;
        while (cycles++ < 6 && !_isStopped())
        {
            bool boughtUpgrade = await BuyUpgradesAsync(cancellationToken).ConfigureAwait(false);
            if (!boughtUpgrade)
            {
                break;
            }

            await BuyEquipmentAsync(cancellationToken).ConfigureAwait(false);
        }

        await ClickAsync(1222, 677, cancellationToken).ConfigureAwait(false);
        _log.Info("Auto upgrade finished.");
    }

    private async Task<bool> BuyUpgradesAsync(CancellationToken cancellationToken)
    {
        await ClickAsync(927, 683, cancellationToken).ConfigureAwait(false);
        await DelayAsync(150, cancellationToken).ConfigureAwait(false);
        for (int i = 0; i < 12; i++)
        {
            await ScrollAsync(1254, 172, 20, cancellationToken).ConfigureAwait(false);
            GameFrame? frame = await CaptureAsync(cancellationToken).ConfigureAwait(false);
            if (frame is null || !HasColor(frame, 1246, 158, 1262, 176, 0xD6, 0xD6, 0xD6, 24))
            {
                break;
            }
        }

        await DelayAsync(300, cancellationToken).ConfigureAwait(false);
        bool bought = false;
        int y = 170;
        for (int attempt = 0; attempt < 40 && y < 620; attempt++)
        {
            GameFrame? frame = await CaptureAsync(cancellationToken).ConfigureAwait(false);
            if (frame is null)
            {
                break;
            }

            if (HasColor(frame, 882, y, 909, y + 72, 0xF4, 0xB4, 0x1B, 24)
                || HasColor(frame, 882, y, 909, y + 72, 0xE4, 0x78, 0xFF, 24))
            {
                y += 96;
                continue;
            }

            bool available = HasColor(frame, 1168, y, 1192, y + 22, 0x11, 0xA6, 0x22, 26)
                || HasColor(frame, 1168, y, 1192, y + 22, 0x0C, 0x74, 0x18, 26);
            if (!available)
            {
                break;
            }

            bought = await ClickAsync(1180, y, cancellationToken).ConfigureAwait(false) || bought;
            await DelayAsync(75, cancellationToken).ConfigureAwait(false);
        }

        return bought;
    }

    private async Task BuyEquipmentAsync(CancellationToken cancellationToken)
    {
        await ClickAsync(850, 690, cancellationToken).ConfigureAwait(false);
        await DelayAsync(75, cancellationToken).ConfigureAwait(false);
        await ClickRepeatedAsync(1180, 636, 4, cancellationToken).ConfigureAwait(false);

        GameFrame? frame = await CaptureAsync(cancellationToken).ConfigureAwait(false);
        if (frame is not null && HasColor(frame, 1245, 328, 1270, 352, 0x11, 0xAA, 0x23, 26))
        {
            await ClickRepeatedAsync(1200, 200, 5, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await ClickRepeatedAsync(1253, 592, 5, cancellationToken).ConfigureAwait(false);
            await DelayAsync(200, cancellationToken).ConfigureAwait(false);
        }

        for (int attempt = 0; attempt < 80 && !_isStopped(); attempt++)
        {
            frame = await CaptureAsync(cancellationToken).ConfigureAwait(false);
            (int X, int Y)? green = frame is null
                ? null
                : FindColor(frame, 1148, 170, 1174, 590, 0x11, 0xAA, 0x23, 26);
            if (green is not null)
            {
                await ClickAsync(850, 690, cancellationToken).ConfigureAwait(false);
                await ClickRepeatedAsync(green.Value.X, green.Value.Y, 5, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (frame is null || !HasColor(frame, 1248, 158, 1270, 180, 0xD6, 0xD6, 0xD6, 24))
            {
                break;
            }

            await ScrollAsync(1260, 170, 1, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task AutoAscendAsync(CancellationToken cancellationToken)
    {
        _log.Info("Auto ascend started.");
        GameFrame? frame = await CaptureAsync(cancellationToken).ConfigureAwait(false);
        bool dialogOpen = frame is not null
            && HasColor(frame, 245, 465, 275, 615, 0x58, 0x18, 0x8D, 24);
        if (!dialogOpen)
        {
            await ClickAsync(95, 90, cancellationToken).ConfigureAwait(false);
            await DelayAsync(400, cancellationToken).ConfigureAwait(false);
            await ClickAsync(93, 680, cancellationToken).ConfigureAwait(false);
            await DelayAsync(400, cancellationToken).ConfigureAwait(false);
        }

        frame = await CaptureAsync(cancellationToken).ConfigureAwait(false);
        if (frame is not null && HasColor(frame, 245, 460, 275, 615, 0x58, 0x18, 0x8D, 28))
        {
            await ClickAsync(260, 600, cancellationToken).ConfigureAwait(false);
            await DelayAsync(300, cancellationToken).ConfigureAwait(false);
            await ClickAsync(550, 580, cancellationToken).ConfigureAwait(false);
            await DelayAsync(300, cancellationToken).ConfigureAwait(false);
            await AutoUpgradeAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task CollectMinionsAsync(CancellationToken cancellationToken)
    {
        _log.Info("Collecting minions.");
        await ClickAsync(95, 90, cancellationToken).ConfigureAwait(false);
        await DelayAsync(400, cancellationToken).ConfigureAwait(false);
        foreach ((int X, int Y) point in new[] { (93, 680), (193, 680), (691, 680), (332, 680) })
        {
            await ClickAsync(point.X, point.Y, cancellationToken).ConfigureAwait(false);
            await DelayAsync(200, cancellationToken).ConfigureAwait(false);
        }

        GameFrame? frame = await CaptureAsync(cancellationToken).ConfigureAwait(false);
        bool dailyBonus = frame is not null
            && HasColor(frame, 370, 410, 910, 470, 0x11, 0xAA, 0x23, 28);
        if (dailyBonus)
        {
            await ClickRepeatedAsync(320, 280, 5, cancellationToken).ConfigureAwait(false);
            await DelayAsync(200, cancellationToken).ConfigureAwait(false);
            await ClickRepeatedAsync(320, 280, 5, cancellationToken).ConfigureAwait(false);
            await DelayAsync(200, cancellationToken).ConfigureAwait(false);
            await ClickRepeatedAsync(320, 180, 5, cancellationToken).ConfigureAwait(false);
            _log.Info("Minions collected with the daily bonus.");
        }
        else
        {
            await ClickRepeatedAsync(318, 182, 5, cancellationToken).ConfigureAwait(false);
            await DelayAsync(200, cancellationToken).ConfigureAwait(false);
            await ClickRepeatedAsync(318, 182, 5, cancellationToken).ConfigureAwait(false);
            _log.Info("Minions collected.");
        }

        await ClickAsync(570, 694, cancellationToken).ConfigureAwait(false);
    }

    private async Task ClaimQuestsAsync(CancellationToken cancellationToken)
    {
        _log.Info("Claiming quests.");
        foreach ((int X, int Y, int Delay) step in new[]
        {
            (1244, 712, 150), (1163, 655, 150), (850, 690, 0),
            (927, 683, 150), (1000, 690, 100)
        })
        {
            await ClickAsync(step.X, step.Y, cancellationToken).ConfigureAwait(false);
            await DelayAsync(step.Delay, cancellationToken).ConfigureAwait(false);
        }

        for (int i = 0; i < 12; i++)
        {
            await ScrollAsync(1254, 272, 20, cancellationToken).ConfigureAwait(false);
            GameFrame? topFrame = await CaptureAsync(cancellationToken).ConfigureAwait(false);
            if (topFrame is null || !HasColor(topFrame, 1244, 257, 1264, 278, 0xD6, 0xD6, 0xD6, 24))
            {
                break;
            }
        }

        await DelayAsync(500, cancellationToken).ConfigureAwait(false);
        for (int i = 0; i < 80; i++)
        {
            GameFrame? frame = await CaptureAsync(cancellationToken).ConfigureAwait(false);
            (int X, int Y)? claim = frame is null
                ? null
                : FindColor(frame, 1148, 270, 1175, 590, 0x11, 0xA6, 0x22, 28);
            if (claim is not null)
            {
                await ClickAsync(claim.Value.X, claim.Value.Y, cancellationToken).ConfigureAwait(false);
                _log.Info("Quest claimed.");
                continue;
            }

            if (frame is null
                || HasColor(frame, 1255, 646, 1278, 670, 0xFF, 0xFF, 0xFF, 18)
                || !HasColor(frame, 1255, 646, 1278, 670, 0xA0, 0xA0, 0xA0, 28))
            {
                break;
            }

            await ScrollAsync(1267, 270, -1, cancellationToken).ConfigureAwait(false);
            await DelayAsync(100, cancellationToken).ConfigureAwait(false);
        }

        await ClickAsync(1244, 712, cancellationToken).ConfigureAwait(false);
    }

    private async Task CirclePortalsAsync(CancellationToken cancellationToken)
    {
        AutomationSettings settings = _settingsProvider();
        _log.Info($"Cycling portal destination {settings.PortalCyclePosition}.");
        await ClickAsync(1180, 150, cancellationToken).ConfigureAwait(false);
        await DelayAsync(300, cancellationToken).ConfigureAwait(false);
        for (int i = 0; i < 15; i++)
        {
            await ScrollAsync(867, 300, 20, cancellationToken).ConfigureAwait(false);
            GameFrame? frame = await CaptureAsync(cancellationToken).ConfigureAwait(false);
            if (frame is null || !HasColor(frame, 865, 240, 885, 260, 0xD6, 0xD6, 0xD6, 24))
            {
                break;
            }
        }

        await DelayAsync(300, cancellationToken).ConfigureAwait(false);
        (byte R, byte G, byte B) destination = GetPortalColor(settings.PortalCyclePosition);
        bool selected = false;
        for (int i = 0; i < 70; i++)
        {
            GameFrame? frame = await CaptureAsync(cancellationToken).ConfigureAwait(false);
            (int X, int Y)? location = frame is null
                ? null
                : FindColor(frame, 458, 220, 485, 550, destination.R, destination.G, destination.B, 28);
            if (location is not null)
            {
                await ScrollAsync(867, 300, -2, cancellationToken).ConfigureAwait(false);
                await ClickAsync(location.Value.X + 300, location.Value.Y, cancellationToken).ConfigureAwait(false);
                selected = true;
                break;
            }

            if (frame is null || !HasColor(frame, 865, 525, 885, 548, 0xD6, 0xD6, 0xD6, 24))
            {
                break;
            }

            await ScrollAsync(867, 300, -1, cancellationToken).ConfigureAwait(false);
        }

        if (!selected)
        {
            await ClickAsync(600, 600, cancellationToken).ConfigureAwait(false);
        }

        settings.PortalCyclePosition = settings.PortalCyclePosition >= 9
            ? 1
            : settings.PortalCyclePosition + 1;
        _settingsChanged();
        _log.Info(selected ? "Portal destination selected." : "Portal selection closed without a match.");
        await DelayAsync(10_000, cancellationToken).ConfigureAwait(false);
    }

    private async Task RageWhenHordeAsync(CancellationToken cancellationToken)
    {
        AutomationSettings settings = _settingsProvider();
        GameFrame? frame = await CaptureAsync(cancellationToken).ConfigureAwait(false);
        bool soulBonus = frame is not null && HasSoulBonus(frame);
        if (soulBonus)
        {
            _log.Info("Mega Horde and Soul Bonus detected.");
            if (settings.CraftRagePill)
            {
                await BuyTemporaryItemAsync(0x87, 0x16, 0x46, cancellationToken).ConfigureAwait(false);
            }

            if (settings.CraftSoulsCompass)
            {
                await BuyTemporaryItemAsync(0x7D, 0x55, 0xD8, cancellationToken).ConfigureAwait(false);
            }
        }

        if (!settings.DisableRageWithoutSoulBonus || soulBonus)
        {
            if (settings.CraftDimensionalStaff)
            {
                await BuyTemporaryItemAsync(0xF3, 0x7C, 0x55, cancellationToken).ConfigureAwait(false);
                settings.CraftDimensionalStaff = false;
                _settingsChanged();
            }

            if (settings.CraftBidimensionalStaff)
            {
                await BuyTemporaryItemAsync(0x52, 0x66, 0x29, cancellationToken).ConfigureAwait(false);
                settings.CraftBidimensionalStaff = false;
                _settingsChanged();
            }

            await PressRageAsync(cancellationToken).ConfigureAwait(false);
            _log.Info("Mega Horde rage command sent.");
        }
    }

    private async Task BuyTemporaryItemAsync(
        byte red,
        byte green,
        byte blue,
        CancellationToken cancellationToken)
    {
        await ClickAsync(160, 100, cancellationToken).ConfigureAwait(false);
        await DelayAsync(150, cancellationToken).ConfigureAwait(false);
        await ClickAsync(260, 690, cancellationToken).ConfigureAwait(false);
        await DelayAsync(150, cancellationToken).ConfigureAwait(false);
        GameFrame? frame = await CaptureAsync(cancellationToken).ConfigureAwait(false);
        (int X, int Y)? item = frame is null
            ? null
            : FindColor(frame, 43, 330, 625, 630, red, green, blue, 28);
        if (item is not null)
        {
            await ClickAsync(item.Value.X, item.Value.Y, cancellationToken).ConfigureAwait(false);
            await DelayAsync(200, cancellationToken).ConfigureAwait(false);
            await ClickAsync(420, 156, cancellationToken).ConfigureAwait(false);
            _log.Info("Temporary crafting item activated.");
        }
        else
        {
            _log.Info("Temporary crafting item was unavailable.");
        }

        await ClickAsync(440, 690, cancellationToken).ConfigureAwait(false);
        await DelayAsync(100, cancellationToken).ConfigureAwait(false);
    }

    private async Task PlayAscendingHeightsAsync(CancellationToken cancellationToken)
    {
        _log.Info("Ascending Heights detected.");
        await ClearOpeningSliderAsync(
            frame => HasColor(frame, 620, 225, 655, 650, 0xFF, 0xCC, 0x66, 24),
            cancellationToken).ConfigureAwait(false);
        await DelayAsync(3900, cancellationToken).ConfigureAwait(false);
        DateTime deadline = DateTime.UtcNow.AddMinutes(4);
        (int X, int Y)? previousPlatform = null;
        int sameCount = 0;
        while (DateTime.UtcNow < deadline && !_isStopped())
        {
            GameFrame? frame = await CaptureAsync(cancellationToken).ConfigureAwait(false);
            if (frame is null)
            {
                await WaitUntilReadyAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (HasColor(frame, 175, 110, 205, 140, 0xFF, 0xAF, 0x36, 24))
            {
                _log.Info("Ascending Heights ended.");
                return;
            }

            if (HasColor(frame, 525, 545, 575, 580, 0x00, 0xA4, 0x00, 28))
            {
                await DelayAsync(2000, cancellationToken).ConfigureAwait(false);
                await ClickAsync(560, 570, cancellationToken).ConfigureAwait(false);
            }

            if (HasColor(frame, 690, 372, 740, 400, 0x7A, 0x44, 0x4A, 26))
            {
                await ClickAsync(1100, 600, cancellationToken).ConfigureAwait(false);
                _log.Info("Ascending Heights completed.");
                return;
            }

            (int X, int Y)? player = FindColor(frame, 375, 250, 900, 719, 0x63, 0x3E, 0x75, 26);
            if (player is null)
            {
                continue;
            }

            int left = sameCount >= 5 ? 375 : Math.Max(375, player.Value.X - 130);
            int right = sameCount >= 5 ? 900 : Math.Min(900, player.Value.X + 130);
            (int X, int Y)? platform = FindColor(
                frame, left, Math.Min(719, player.Value.Y + 50), right, 719,
                0xC0, 0xCB, 0xDC, 28)
                ?? FindColor(frame, 375, Math.Min(719, player.Value.Y + 50), 900, 719,
                    0xC0, 0xCB, 0xDC, 28);
            if (platform is null)
            {
                continue;
            }

            sameCount = previousPlatform == platform ? sameCount + 1 : 0;
            previousPlatform = platform;
            if (platform.Value.X + 35 > player.Value.X + 35)
            {
                await ClickAsync(1100, 600, cancellationToken).ConfigureAwait(false);
            }
            else if (platform.Value.X + 35 < player.Value.X - 35)
            {
                await ClickAsync(100, 600, cancellationToken).ConfigureAwait(false);
            }
        }

        _log.Info("Ascending Heights timed out; automation remains running.");
    }

    private async Task PlayVictorAsync(CancellationToken cancellationToken)
    {
        _log.Info("Victor boss fight detected.");
        await ClearOpeningSliderAsync(
            frame => HasColor(frame, 640, 210, 668, 235, 0xFF, 0xF3, 0x8F, 24),
            cancellationToken).ConfigureAwait(false);
        await DelayAsync(5000, cancellationToken).ConfigureAwait(false);
        DateTime deadline = DateTime.UtcNow.AddSeconds(200);
        bool secondStage = false;
        while (DateTime.UtcNow < deadline && !_isStopped())
        {
            GameFrame? frame = await CaptureAsync(cancellationToken).ConfigureAwait(false);
            if (frame is null)
            {
                await WaitUntilReadyAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (HasColor(frame, 820, 462, 850, 492, 0xFD, 0x31, 0x69, 28))
            {
                await DelayAsync(500, cancellationToken).ConfigureAwait(false);
                await ClickAsync(615, 563, cancellationToken).ConfigureAwait(false);
                _log.Info("Victor boss fight completed.");
                return;
            }

            if (!secondStage && HasColor(frame, 255, 115, 290, 145, 0xAE, 0x6C, 0x37, 28))
            {
                secondStage = true;
                _log.Info("Victor stage 2 detected.");
            }

            (int X, int Y)? attack = FindColor(frame, 910, 280, 930, 465, 0xFF, 0xFF, 0xFF, 18);
            if (attack is not null)
            {
                if (attack.Value.Y > 370)
                {
                    await DelayAsync(200, cancellationToken).ConfigureAwait(false);
                    await JumpBurstAsync(secondStage ? 18 : 1, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await DelayAsync(730, cancellationToken).ConfigureAwait(false);
                    await JumpBurstAsync(secondStage ? 15 : 1, cancellationToken).ConfigureAwait(false);
                }
            }
            else if (secondStage && HasColor(frame, 1060, 140, 1082, 500, 0xFF, 0xFF, 0xFF, 18))
            {
                await DelayAsync(350, cancellationToken).ConfigureAwait(false);
                await _controlAdapter.HoldJumpAsync(100, cancellationToken).ConfigureAwait(false);
                await JumpBurstAsync(18, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _controlAdapter.HoldJumpAsync(35, cancellationToken).ConfigureAwait(false);
                _inputExecuted();
                await DelayAsync(50, cancellationToken).ConfigureAwait(false);
            }
        }

        _log.Info("Victor boss fight timed out; automation remains running.");
    }

    private async Task PlayKnightAsync(CancellationToken cancellationToken)
    {
        _log.Info("Knight boss fight detected.");
        await ClearOpeningSliderAsync(
            frame => HasColor(frame, 640, 210, 668, 235, 0xFF, 0xF3, 0x8F, 24),
            cancellationToken).ConfigureAwait(false);
        for (int i = 0; i < 9; i++)
        {
            await DelayAsync(200, cancellationToken).ConfigureAwait(false);
            await _controlAdapter.HoldJumpAsync(300, cancellationToken).ConfigureAwait(false);
            _inputExecuted();
            await JumpBurstAsync(14, cancellationToken).ConfigureAwait(false);
        }

        DateTime deadline = DateTime.UtcNow.AddSeconds(200);
        bool dashAttack = false;
        while (DateTime.UtcNow < deadline && !_isStopped())
        {
            GameFrame? frame = await CaptureAsync(cancellationToken).ConfigureAwait(false);
            if (frame is null)
            {
                await WaitUntilReadyAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (HasColor(frame, 520, 62, 550, 90, 0x00, 0x00, 0x00, 8))
            {
                for (int i = 0; i < 60; i++)
                {
                    await ClickAsync(1000, 328, cancellationToken).ConfigureAwait(false);
                    frame = await CaptureAsync(cancellationToken).ConfigureAwait(false);
                    if (frame is not null && HasColor(frame, 820, 462, 850, 492, 0xFD, 0x31, 0x69, 28))
                    {
                        await ClickAsync(615, 563, cancellationToken).ConfigureAwait(false);
                        _log.Info("Knight boss fight completed.");
                        return;
                    }
                }
            }

            if (frame is null)
            {
                continue;
            }

            if (HasColor(frame, 252, 115, 290, 145, 0xF5, 0xB7, 0x84, 28))
            {
                dashAttack = true;
                await ClickAsync(1020, 420, cancellationToken).ConfigureAwait(false);
            }

            (int X, int Y)? ranged = FindColor(frame, 890, 140, 910, 355, 0x2C, 0x2C, 0x2C, 18);
            (int X, int Y)? close = FindColor(frame, 435, 200, 455, 400, 0xAF, 0x99, 0x67, 24)
                ?? FindColor(frame, 435, 200, 455, 400, 0xD7, 0xCC, 0xB3, 24);
            (int X, int Y)? attack = ranged ?? close;
            if (attack is not null)
            {
                await DelayAsync(ranged is null ? 700 : 150, cancellationToken).ConfigureAwait(false);
                if (attack.Value.Y <= (ranged is null ? 283 : 240))
                {
                    await DelayAsync(730, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await _controlAdapter.HoldJumpAsync(170, cancellationToken).ConfigureAwait(false);
                    _inputExecuted();
                }

                await ClickRepeatedAsync(1020, 420, dashAttack ? 11 : 25, cancellationToken).ConfigureAwait(false);
            }
        }

        _log.Info("Knight boss fight timed out; automation remains running.");
    }

    private async Task ClearOpeningSliderAsync(
        Func<GameFrame, bool> isVisible,
        CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline && !_isStopped())
        {
            GameFrame? frame = await CaptureAsync(cancellationToken).ConfigureAwait(false);
            if (frame is null || !isVisible(frame))
            {
                return;
            }

            if (HasColor(frame, 435, 550, 450, 570, 0x00, 0x7E, 0x00, 20))
            {
                await DragAsync(840, 560, 450, 560, cancellationToken).ConfigureAwait(false);
            }
            else if (HasColor(frame, 435, 610, 450, 630, 0x00, 0x7E, 0x00, 20))
            {
                await DragAsync(840, 620, 450, 620, cancellationToken).ConfigureAwait(false);
            }
            else if (HasColor(frame, 840, 550, 860, 570, 0x00, 0x7E, 0x00, 20))
            {
                await DragAsync(450, 560, 840, 560, cancellationToken).ConfigureAwait(false);
            }
            else if (HasColor(frame, 840, 610, 860, 630, 0x00, 0x7E, 0x00, 20))
            {
                await DragAsync(450, 620, 840, 620, cancellationToken).ConfigureAwait(false);
            }

            await DelayAsync(500, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task JumpBurstAsync(int count, CancellationToken cancellationToken)
    {
        for (int i = 0; i < count && !_isStopped(); i++)
        {
            await _controlAdapter.HoldJumpAsync(35, cancellationToken).ConfigureAwait(false);
            _inputExecuted();
            await DelayAsync(10, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task CloseVisibleResultAsync(GameFrame frame, CancellationToken cancellationToken)
    {
        if (HasColor(frame, 665, 580, 695, 610, 0xAF, 0x00, 0x00, 24))
        {
            await ClickAsync(780, 600, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> ClickAsync(int x, int y, CancellationToken cancellationToken)
    {
        if (!await WaitUntilReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        bool sent = await _pointerAdapter.ClickClientPointAsync(x, y, cancellationToken).ConfigureAwait(false);
        if (sent)
        {
            _inputExecuted();
        }

        return sent;
    }

    private async Task ClickRepeatedAsync(
        int x,
        int y,
        int count,
        CancellationToken cancellationToken)
    {
        for (int i = 0; i < count && !_isStopped(); i++)
        {
            await ClickAsync(x, y, cancellationToken).ConfigureAwait(false);
            await DelayAsync(20, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ScrollAsync(
        int x,
        int y,
        int notches,
        CancellationToken cancellationToken)
    {
        if (!await WaitUntilReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (await _controlAdapter.ScrollClientPointAsync(x, y, notches, cancellationToken).ConfigureAwait(false))
        {
            _inputExecuted();
        }
    }

    private async Task DragAsync(
        int startX,
        int startY,
        int endX,
        int endY,
        CancellationToken cancellationToken)
    {
        if (!await WaitUntilReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (await _controlAdapter.DragClientPointAsync(
            startX, startY, endX, endY, cancellationToken).ConfigureAwait(false))
        {
            _inputExecuted();
        }
    }

    private async Task PressRageAsync(CancellationToken cancellationToken)
    {
        if (!await WaitUntilReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (await _controlAdapter.PressRageAsync(cancellationToken).ConfigureAwait(false))
        {
            _inputExecuted();
        }
    }

    private async Task<bool> WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        while (!_isStopped())
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

    private async Task<bool> DelayAsync(int milliseconds, CancellationToken cancellationToken)
    {
        int remaining = Math.Max(0, milliseconds);
        while (remaining > 0)
        {
            if (_isStopped())
            {
                return false;
            }

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

        return !_isStopped();
    }

    private Task<GameFrame?> CaptureAsync(CancellationToken cancellationToken) =>
        _frameSource.CaptureAsync(cancellationToken);

    private static bool HasSoulBonus(GameFrame frame) =>
        HasColor(frame, 615, 130, 640, 225, 0xA8, 0x6D, 0x0A, 28);

    private static bool IsPortalReady(GameFrame frame)
    {
        bool button = HasColor(frame, 1165, 150, 1195, 182, 0x83, 0x03, 0x99, 28)
            || HasColor(frame, 1165, 150, 1195, 182, 0x29, 0x01, 0x30, 20);
        bool timerVisible = HasColor(frame, 1145, 135, 1220, 165, 0xFF, 0xFF, 0xFF, 18);
        return button && !timerVisible;
    }

    private static bool IsBossEntrance(GameFrame frame) =>
        HasColor(frame, 620, 205, 660, 245, 0xFF, 0x87, 0x8A, 28)
        && HasColor(frame, 615, 205, 655, 245, 0xF2, 0x63, 0xBD, 28)
        && HasColor(frame, 625, 205, 665, 245, 0xFF, 0xF3, 0x8F, 28);

    private static bool IsKnightBoss(GameFrame frame) =>
        HasColor(frame, 15, 675, 45, 710, 0x0B, 0x03, 0x03, 14);

    private static bool IsAscendingHeightsEntrance(GameFrame frame) =>
        HasColor(frame, 650, 195, 690, 235, 0xC2, 0xF4, 0xF9, 26)
        && HasColor(frame, 620, 225, 660, 650, 0xFF, 0xCC, 0x66, 26);

    private static bool IsChestHuntEntrance(GameFrame frame) =>
        HasColor(frame, 170, 280, 205, 310, 0xFF, 0xBB, 0x31, 28)
        && HasColor(frame, 170, 290, 205, 320, 0xF6, 0x8F, 0x37, 28);

    private static bool HasColor(
        GameFrame frame,
        int left,
        int top,
        int right,
        int bottom,
        byte red,
        byte green,
        byte blue,
        int tolerance) =>
        BonusStageVisualDetector.ContainsColor(
            frame, left, top, right, bottom, red, green, blue, tolerance);

    private static (int X, int Y)? FindColor(
        GameFrame frame,
        int left,
        int top,
        int right,
        int bottom,
        byte red,
        byte green,
        byte blue,
        int tolerance)
    {
        int firstX = Math.Max(0, Math.Min(left, right));
        int lastX = Math.Min(frame.Width - 1, Math.Max(left, right));
        int firstY = Math.Max(0, Math.Min(top, bottom));
        int lastY = Math.Min(frame.Height - 1, Math.Max(top, bottom));
        for (int y = firstY; y <= lastY; y++)
        {
            for (int x = firstX; x <= lastX; x++)
            {
                RgbColor actual = frame.GetPixel(x, y);
                if (Math.Abs(actual.Red - red) <= tolerance
                    && Math.Abs(actual.Green - green) <= tolerance
                    && Math.Abs(actual.Blue - blue) <= tolerance)
                {
                    return (x, y);
                }
            }
        }

        return null;
    }

    private static (byte R, byte G, byte B) GetPortalColor(int position) => position switch
    {
        1 => (0x00, 0xCB, 0xF8),
        2 => (0xBD, 0x43, 0x48),
        3 => (0x00, 0x9D, 0x93),
        4 => (0x6F, 0xF5, 0xF8),
        5 => (0xB3, 0x62, 0xC7),
        6 => (0x00, 0x01, 0x73),
        7 => (0x00, 0xF8, 0xB5),
        8 => (0xE1, 0x98, 0xBF),
        _ => (0x4F, 0x00, 0x85)
    };
}
