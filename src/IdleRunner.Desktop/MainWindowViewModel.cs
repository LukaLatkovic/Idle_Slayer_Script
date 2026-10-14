using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private readonly AutomationEngine _engine;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _engineTask;
    private string _statusTitle = "Paused";
    private string _statusDetail = "Waiting for the first Idle Slayer window check.";
    private Brush _statusBrush = PausedBrush;
    private bool _isRunning;
    private bool _isPrimaryActionEnabled = true;
    private int _commandCount;
    private GameSnapshot? _lastSnapshot;

    public MainWindowViewModel(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _settingsStore = new AutomationSettingsStore();
        _settings = _settingsStore.Load().Normalize();
        _log = new DesktopAutomationLog();
        _log.EntryWritten += OnLogEntryWritten;
        _gameAdapter = new WindowsGameAdapter();
        _engine = new AutomationEngine(_gameAdapter, _log, () => _settings);
        _engine.SnapshotChanged += OnSnapshotChanged;
        _engine.PauseChanged += OnPauseChanged;
        _engine.ActionExecuted += OnActionExecuted;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> ActivityEntries { get; } = new();

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
            OnPropertyChanged();
            OnPropertyChanged(nameof(JumpIntervalLabel));
        }
    }

    public string JumpIntervalLabel => $"{JumpIntervalMilliseconds} ms";

    public void Start()
    {
        _engineTask ??= _engine.RunAsync(_shutdown.Token);
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

    public async Task ShutdownAsync()
    {
        _engine.SetPaused(true);
        await _gameAdapter.ReleaseInputAsync(CancellationToken.None);
        _settingsStore.Save(_settings.Normalize());
        _shutdown.Cancel();

        if (_engineTask is not null)
        {
            await _engineTask.ConfigureAwait(true);
        }

        _engine.SnapshotChanged -= OnSnapshotChanged;
        _engine.PauseChanged -= OnPauseChanged;
        _engine.ActionExecuted -= OnActionExecuted;
        _log.EntryWritten -= OnLogEntryWritten;
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

            if (_commandCount == 1)
            {
                _log.Info("The first input pulse was sent to Idle Slayer.");
            }
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
