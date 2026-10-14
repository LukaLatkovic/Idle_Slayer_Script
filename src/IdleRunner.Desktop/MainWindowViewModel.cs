using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Threading;
using IdleRunner.Core;
using IdleRunner.Platform.Windows;

namespace IdleRunner.Desktop;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private static readonly Brush PausedBrush = CreateBrush("#7A818D");
    private static readonly Brush WaitingBrush = CreateBrush("#F0B85A");
    private static readonly Brush ReadyBrush = CreateBrush("#D6FF52");
    private readonly Dispatcher _dispatcher;
    private readonly AutomationSettingsStore _settingsStore;
    private readonly AutomationSettings _settings;
    private readonly DesktopAutomationLog _log;
    private readonly IGameAdapter _gameAdapter;
    private readonly IGameFrameSource _frameSource;
    private readonly GameplayVisualGate _gameplayVisualGate;
    private readonly SilverBoxVisualService _silverBoxVisualService;
    private readonly ChestHuntVisualService _chestHuntVisualService;
    private readonly AutomationEngine _engine;
    private readonly ChestHuntAutomationService _chestHuntAutomationService;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _engineTask;
    private Task? _chestHuntTask;
    private string _statusTitle = "Paused";
    private string _statusDetail = "Waiting for the first Idle Slayer window check.";
    private Brush _statusBrush = PausedBrush;
    private bool _isRunning;
    private bool _isPrimaryActionEnabled = true;
    private int _commandCount;
    private GameSnapshot? _lastSnapshot;
    private bool _frameCaptureReported;
    private bool _isCalibrationEnabled = true;
    private bool _jumpPulseReported;

    public MainWindowViewModel(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _settingsStore = new AutomationSettingsStore();
        _settings = _settingsStore.Load().Normalize();
        _log = new DesktopAutomationLog();
        _log.EntryWritten += OnLogEntryWritten;

        if (_settingsStore.ImportedLegacySettingsPath is not null)
        {
            _log.Info($"Imported legacy settings from {_settingsStore.ImportedLegacySettingsPath}.");
        }

        WindowsGameAdapter windowsGameAdapter = new();
        _gameAdapter = windowsGameAdapter;
        _frameSource = new WindowsGameFrameSource();
        _gameplayVisualGate = new GameplayVisualGate(
            _frameSource,
            new GameplayCalibrationStore());
        _silverBoxVisualService = new SilverBoxVisualService(
            _frameSource,
            new SilverBoxCalibrationStore());
        _chestHuntVisualService = new ChestHuntVisualService(
            _frameSource,
            new ChestHuntCalibrationStore());
        _engine = new AutomationEngine(
            _gameAdapter,
            _log,
            () => _settings,
            modules:
            [
                new SilverBoxModule(_frameSource, () => _silverBoxVisualService.Observation),
                new JumpAndBoostModule()
            ],
            gate: _gameplayVisualGate);
        _engine.SnapshotChanged += OnSnapshotChanged;
        _engine.PauseChanged += OnPauseChanged;
        _engine.ActionExecuted += OnActionExecuted;
        _chestHuntAutomationService = new ChestHuntAutomationService(
            _gameAdapter,
            windowsGameAdapter,
            _frameSource,
            _chestHuntVisualService,
            _log,
            () => _settings,
            () => _engine.IsPaused,
            () => _engine.SetPaused(true),
            OnChestHuntClickExecuted);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> ActivityEntries { get; } = new();

    public string ActivityText => string.Join(Environment.NewLine, ActivityEntries);

    public string StatusTitle
    {
        get => _statusTitle;
        private set => SetField(ref _statusTitle, value);
    }

    public string StatusDetail
    {
        get => _statusDetail;
        private set => SetField(ref _statusDetail, value);
    }

    public Brush StatusBrush
    {
        get => _statusBrush;
        private set => SetField(ref _statusBrush, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetField(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(PrimaryActionText));
                RefreshStatus();
            }
        }
    }

    public string PrimaryActionText => IsRunning ? "Pause automation" : "Start automation";

    public bool IsPrimaryActionEnabled
    {
        get => _isPrimaryActionEnabled;
        private set => SetField(ref _isPrimaryActionEnabled, value);
    }

    public bool IsCalibrationEnabled
    {
        get => _isCalibrationEnabled;
        private set => SetField(ref _isCalibrationEnabled, value);
    }

    public string GameplayCalibrationStatus => _gameplayVisualGate.HasCalibration
        ? "Calibrated - visual input safety is active."
        : "Not calibrated - visual input safety is inactive.";

    public string SilverBoxCalibrationStatus => _silverBoxVisualService.HasCalibration
        ? "Icon calibrated - collector is ready."
        : "Icon not calibrated - collector will not click.";

    public string ChestHuntCalibrationStatus => _chestHuntVisualService.HasCalibration
        ? "Ready - Chest Hunts will run automatically."
        : "Not calibrated - capture while the yellow Saver marker is visible.";

    public string CommandCountLabel => _commandCount == 1
        ? "1 command sent"
        : $"{_commandCount:N0} commands sent";

    public int JumpIntervalMilliseconds
    {
        get => _settings.JumpIntervalMilliseconds;
        set
        {
            int normalized = Math.Max(
                AutomationSettings.MinimumIntervalMilliseconds,
                Math.Min(AutomationSettings.MaximumIntervalMilliseconds, value));

            if (_settings.JumpIntervalMilliseconds == normalized)
            {
                return;
            }

            _settings.JumpIntervalMilliseconds = normalized;
            PersistSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(JumpIntervalLabel));
        }
    }

    public string JumpIntervalLabel => $"{JumpIntervalMilliseconds} ms";

    public bool CollectSilverBoxesEnabled
    {
        get => _settings.CollectSilverBoxesEnabled;
        set => SetPersistedSetting(
            _settings.CollectSilverBoxesEnabled,
            value,
            next => _settings.CollectSilverBoxesEnabled = next);
    }

    public bool AutoBuyUpgradesEnabled
    {
        get => _settings.AutoBuyUpgradesEnabled;
        set => SetPersistedSetting(
            _settings.AutoBuyUpgradesEnabled,
            value,
            next => _settings.AutoBuyUpgradesEnabled = next);
    }

    public int AutoBuyIntervalMinutes
    {
        get => _settings.AutoBuyIntervalMinutes;
        set => SetPersistedSetting(
            _settings.AutoBuyIntervalMinutes,
            NormalizeTimerMinutes(value),
            next => _settings.AutoBuyIntervalMinutes = next);
    }

    public bool AutoAscendEnabled
    {
        get => _settings.AutoAscendEnabled;
        set => SetPersistedSetting(
            _settings.AutoAscendEnabled,
            value,
            next => _settings.AutoAscendEnabled = next);
    }

    public int AutoAscendIntervalMinutes
    {
        get => _settings.AutoAscendIntervalMinutes;
        set => SetPersistedSetting(
            _settings.AutoAscendIntervalMinutes,
            NormalizeTimerMinutes(value),
            next => _settings.AutoAscendIntervalMinutes = next);
    }

    public bool CirclePortalsEnabled
    {
        get => _settings.CirclePortalsEnabled;
        set => SetPersistedSetting(
            _settings.CirclePortalsEnabled,
            value,
            next => _settings.CirclePortalsEnabled = next);
    }

    public int PortalCyclePosition
    {
        get => _settings.PortalCyclePosition;
        set => SetPersistedSetting(
            _settings.PortalCyclePosition,
            Math.Max(1, Math.Min(9, value)),
            next => _settings.PortalCyclePosition = next);
    }

    public bool DisableRageWithoutSoulBonus
    {
        get => _settings.DisableRageWithoutSoulBonus;
        set => SetPersistedSetting(
            _settings.DisableRageWithoutSoulBonus,
            value,
            next => _settings.DisableRageWithoutSoulBonus = next);
    }

    public bool SkipBonusStage
    {
        get => _settings.SkipBonusStage;
        set => SetPersistedSetting(
            _settings.SkipBonusStage,
            value,
            next => _settings.SkipBonusStage = next);
    }

    public bool NoLockpicking
    {
        get => _settings.NoLockpicking;
        set => SetPersistedSetting(
            _settings.NoLockpicking,
            value,
            next => _settings.NoLockpicking = next);
    }

    public bool NoReinforcedCrystalSaver
    {
        get => _settings.NoReinforcedCrystalSaver;
        set => SetPersistedSetting(
            _settings.NoReinforcedCrystalSaver,
            value,
            next => _settings.NoReinforcedCrystalSaver = next);
    }

    public bool PerfectChestHunt
    {
        get => _settings.PerfectChestHunt;
        set => SetPersistedSetting(
            _settings.PerfectChestHunt,
            value,
            next => _settings.PerfectChestHunt = next);
    }

    public bool CraftSoulsCompass
    {
        get => _settings.CraftSoulsCompass;
        set => SetPersistedSetting(
            _settings.CraftSoulsCompass,
            value,
            next => _settings.CraftSoulsCompass = next);
    }

    public bool CraftRagePill
    {
        get => _settings.CraftRagePill;
        set => SetPersistedSetting(
            _settings.CraftRagePill,
            value,
            next => _settings.CraftRagePill = next);
    }

    public bool CraftDimensionalStaff
    {
        get => _settings.CraftDimensionalStaff;
        set => SetPersistedSetting(
            _settings.CraftDimensionalStaff,
            value,
            next => _settings.CraftDimensionalStaff = next);
    }

    public bool CraftBidimensionalStaff
    {
        get => _settings.CraftBidimensionalStaff;
        set => SetPersistedSetting(
            _settings.CraftBidimensionalStaff,
            value,
            next => _settings.CraftBidimensionalStaff = next);
    }

    public void Start()
    {
        _engineTask ??= _engine.RunAsync(_shutdown.Token);
        _chestHuntTask ??= _chestHuntAutomationService.RunAsync(_shutdown.Token);
    }

    public void ReportInfo(string message)
    {
        _log.Info(message);
    }

    public void ReportError(string message, Exception exception)
    {
        _log.Error(message, exception);
    }

    public async Task ToggleAutomationAsync()
    {
        if (!IsPrimaryActionEnabled)
        {
            return;
        }

        if (IsRunning)
        {
            _engine.SetPaused(true);
            await _gameAdapter.ReleaseInputAsync(_shutdown.Token);
            return;
        }

        IsPrimaryActionEnabled = false;
        _engine.SetPaused(false);

        try
        {
            bool activated = await _gameAdapter.TryActivateAsync(_shutdown.Token);
            _log.Info(activated
                ? "Idle Slayer was activated. Input delivery is enabled."
                : "Idle Slayer could not be activated. Focus the game window to enable input delivery.");

            if (activated)
            {
                await VerifyFrameCaptureAsync();
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _log.Error("Idle Slayer could not be activated.", exception);
        }
        finally
        {
            IsPrimaryActionEnabled = true;
        }
    }

    public async Task CalibrateGameplayAsync()
    {
        if (!IsCalibrationEnabled)
        {
            return;
        }

        IsCalibrationEnabled = false;
        _engine.SetPaused(true);

        try
        {
            await _gameAdapter.ReleaseInputAsync(_shutdown.Token);

            bool activated = await _gameAdapter.TryActivateAsync(_shutdown.Token);
            if (!activated)
            {
                _log.Info("Idle Slayer could not be activated for gameplay calibration.");
                return;
            }

            await _gameplayVisualGate.CalibrateAsync(_shutdown.Token);
            OnPropertyChanged(nameof(GameplayCalibrationStatus));
            _log.Info("Gameplay HUD calibrated. Visual input safety is now active.");
            _log.Info("Automation remains paused; press Start automation when ready.");
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _log.Error("Gameplay calibration failed.", exception);
        }
        finally
        {
            await _gameAdapter.ReleaseInputAsync(CancellationToken.None);
            IsCalibrationEnabled = true;
        }
    }

    public async Task CalibrateSilverBoxAsync()
    {
        if (!IsCalibrationEnabled)
        {
            return;
        }

        IsCalibrationEnabled = false;
        _engine.SetPaused(true);

        try
        {
            await _gameAdapter.ReleaseInputAsync(_shutdown.Token);

            bool activated = await _gameAdapter.TryActivateAsync(_shutdown.Token);
            if (!activated)
            {
                _log.Info("Idle Slayer could not be activated for Silver Box calibration.");
                return;
            }

            await _silverBoxVisualService.CalibrateAsync(_shutdown.Token);
            OnPropertyChanged(nameof(SilverBoxCalibrationStatus));
            _log.Info("Silver Box icon calibrated. The collector is ready.");
            _log.Info("Automation remains paused; press Start automation when ready.");
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _log.Error("Silver Box calibration failed.", exception);
        }
        finally
        {
            await _gameAdapter.ReleaseInputAsync(CancellationToken.None);
            IsCalibrationEnabled = true;
        }
    }

    public async Task CalibrateChestHuntAsync()
    {
        if (!IsCalibrationEnabled)
        {
            return;
        }

        IsCalibrationEnabled = false;
        _engine.SetPaused(true);

        try
        {
            await _gameAdapter.ReleaseInputAsync(_shutdown.Token);

            bool activated = await _gameAdapter.TryActivateAsync(_shutdown.Token);
            if (!activated)
            {
                _log.Info("Idle Slayer could not be activated for Chest Hunt calibration.");
                return;
            }

            ChestHuntSaverMatch saver = await _chestHuntVisualService
                .CalibrateAsync(_shutdown.Token);
            OnPropertyChanged(nameof(ChestHuntCalibrationStatus));
            _log.Info(
                $"Chest Hunt layout calibrated. Saver located at row {saver.Position.Row}, "
                + $"column {saver.Position.Column} with confidence {saver.Score:P1}.");
            _log.Info("Chest Hunt automation is ready and will start when a grid is visible.");
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _log.Error("Chest Hunt calibration failed.", exception);
        }
        finally
        {
            await _gameAdapter.ReleaseInputAsync(CancellationToken.None);
            IsCalibrationEnabled = true;
        }
    }

    public async Task ShutdownAsync()
    {
        _engine.SetPaused(true);
        await _gameAdapter.ReleaseInputAsync(CancellationToken.None);
        PersistSettings();
        _shutdown.Cancel();

        if (_engineTask is not null)
        {
            await _engineTask.ConfigureAwait(true);
        }

        if (_chestHuntTask is not null)
        {
            await _chestHuntTask.ConfigureAwait(true);
        }

        _engine.SnapshotChanged -= OnSnapshotChanged;
        _engine.PauseChanged -= OnPauseChanged;
        _engine.ActionExecuted -= OnActionExecuted;
        _log.EntryWritten -= OnLogEntryWritten;
        _gameplayVisualGate.Dispose();
        _shutdown.Dispose();
    }

    private void OnSnapshotChanged(object? sender, GameSnapshot snapshot)
    {
        RunOnUiThread(() =>
        {
            _lastSnapshot = snapshot;
            RefreshStatus();
        });
    }

    private async Task VerifyFrameCaptureAsync()
    {
        if (_frameCaptureReported)
        {
            return;
        }

        GameFrame? frame = await _frameSource.CaptureAsync(_shutdown.Token);
        if (frame is null)
        {
            _log.Info("Visual observation is not available for the current game window.");
            return;
        }

        LuminanceRange luminanceRange = frame.MeasureLuminanceRange(sampleStep: 8);
        if (luminanceRange.Span < 16)
        {
            _log.Info(
                $"Visual observation returned a low-contrast frame "
                + $"({luminanceRange.Darkest}-{luminanceRange.Lightest}); detection remains disabled.");
            return;
        }

        _frameCaptureReported = true;
        _log.Info(
            $"Visual observation is ready: captured a {frame.Width} x {frame.Height} client frame "
            + $"with luminance {luminanceRange.Darkest}-{luminanceRange.Lightest}.");
    }

    private void OnPauseChanged(object? sender, bool isPaused)
    {
        RunOnUiThread(() => IsRunning = !isPaused);
    }

    private void OnActionExecuted(object? sender, AutomationAction action)
    {
        RunOnUiThread(() =>
        {
            _commandCount++;
            OnPropertyChanged(nameof(CommandCountLabel));

            if (action == AutomationAction.CollectSilverBox)
            {
                _log.Info("Silver Box collected.");
            }
            else if (action == AutomationAction.JumpAndBoost && !_jumpPulseReported)
            {
                _jumpPulseReported = true;
                _log.Info("The first input pulse was sent to Idle Slayer.");
            }
        });
    }

    private void OnChestHuntClickExecuted()
    {
        RunOnUiThread(() =>
        {
            _commandCount++;
            OnPropertyChanged(nameof(CommandCountLabel));
        });
    }

    private void OnLogEntryWritten(object? sender, string entry)
    {
        RunOnUiThread(() =>
        {
            ActivityEntries.Insert(0, entry);
            while (ActivityEntries.Count > 100)
            {
                ActivityEntries.RemoveAt(ActivityEntries.Count - 1);
            }

            OnPropertyChanged(nameof(ActivityText));
        });
    }

    private void RefreshStatus()
    {
        if (_lastSnapshot is null)
        {
            StatusTitle = IsRunning ? "Starting" : "Paused";
            StatusDetail = "Waiting for the first Idle Slayer window check.";
            StatusBrush = IsRunning ? WaitingBrush : PausedBrush;
            return;
        }

        bool canReceiveInput = _lastSnapshot.CanReceiveInput(_settings);

        if (!IsRunning)
        {
            StatusTitle = "Paused";
            StatusDetail = canReceiveInput
                ? "Idle Slayer is ready. Start automation when you are ready."
                : _lastSnapshot.Describe(_settings);
            StatusBrush = PausedBrush;
            return;
        }

        StatusTitle = canReceiveInput ? "Running" : "Running - waiting";
        StatusDetail = _lastSnapshot.Describe(_settings);
        StatusBrush = canReceiveInput ? ReadyBrush : WaitingBrush;
    }

    private void RunOnUiThread(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.BeginInvoke(action);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void SetPersistedSetting<T>(
        T currentValue,
        T nextValue,
        Action<T> assign,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(currentValue, nextValue))
        {
            return;
        }

        assign(nextValue);
        PersistSettings();
        OnPropertyChanged(propertyName);
    }

    private void PersistSettings()
    {
        try
        {
            _settingsStore.Save(_settings.Normalize());
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or System.Text.Json.JsonException)
        {
            _log.Error("Settings could not be saved.", exception);
        }
    }

    private static int NormalizeTimerMinutes(int value)
    {
        return Math.Max(
            AutomationSettings.MinimumTimerMinutes,
            Math.Min(AutomationSettings.MaximumTimerMinutes, value));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static Brush CreateBrush(string color)
    {
        SolidColorBrush brush = new((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
