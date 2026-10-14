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
    private static readonly Brush WaitingBrush = CreateBrush("#F0B85A");
    private static readonly Brush ReadyBrush = CreateBrush("#D6FF52");
    private readonly Dispatcher _dispatcher;
    private readonly AutomationSettingsStore _settingsStore;
    private readonly AutomationSettings _settings;
    private readonly DesktopAutomationLog _log;
    private readonly AutomationEngine _engine;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _engineTask;
    private string _statusTitle = "Checking for game";
    private string _statusDetail = "Waiting for the first Idle Slayer window check.";
    private Brush _statusBrush = WaitingBrush;
    private bool _isRunning;

    public MainWindowViewModel(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _settingsStore = new AutomationSettingsStore();
        _settings = _settingsStore.Load().Normalize();
        _log = new DesktopAutomationLog();
        _log.EntryWritten += OnLogEntryWritten;
        _engine = new AutomationEngine(new WindowsGameAdapter(), _log, () => _settings);
        _engine.SnapshotChanged += OnSnapshotChanged;
        _engine.PauseChanged += OnPauseChanged;
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
            }
        }
    }

    public string PrimaryActionText => IsRunning ? "Pause automation" : "Start automation";

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

    public void ToggleAutomation()
    {
        _engine.SetPaused(!IsRunning);
    }

    public async Task ShutdownAsync()
    {
        _engine.SetPaused(true);
        _settingsStore.Save(_settings.Normalize());
        _shutdown.Cancel();

        if (_engineTask is not null)
        {
            await _engineTask.ConfigureAwait(true);
        }

        _engine.SnapshotChanged -= OnSnapshotChanged;
        _engine.PauseChanged -= OnPauseChanged;
        _log.EntryWritten -= OnLogEntryWritten;
        _shutdown.Dispose();
    }

    private void OnSnapshotChanged(object? sender, GameSnapshot snapshot)
    {
        _dispatcher.BeginInvoke(() =>
        {
            bool canReceiveInput = snapshot.CanReceiveInput(_settings);
            StatusTitle = canReceiveInput ? "Ready" : "Waiting";
            StatusDetail = snapshot.Describe(_settings);
            StatusBrush = canReceiveInput ? ReadyBrush : WaitingBrush;
        });
    }

    private void OnPauseChanged(object? sender, bool isPaused)
    {
        _dispatcher.BeginInvoke(() => IsRunning = !isPaused);
    }

    private void OnLogEntryWritten(object? sender, string entry)
    {
        _dispatcher.BeginInvoke(() =>
        {
            ActivityEntries.Insert(0, entry);
            while (ActivityEntries.Count > 100)
            {
                ActivityEntries.RemoveAt(ActivityEntries.Count - 1);
            }
        });
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
