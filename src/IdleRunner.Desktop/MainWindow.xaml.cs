using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;

namespace IdleRunner.Desktop;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private GlobalHotkeyManager? _hotkeyManager;
    private bool _shutdownCompleted;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel(Dispatcher);
        DataContext = _viewModel;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ((Storyboard)Resources["EntranceStoryboard"]).Begin(this);
        _viewModel.Start();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            _hotkeyManager = new GlobalHotkeyManager(this);
            _hotkeyManager.ToggleRequested += OnHotkeyToggleRequested;
            _hotkeyManager.CloseRequested += OnHotkeyCloseRequested;
            _hotkeyManager.AutoUpgradeRequested += OnHotkeyAutoUpgradeRequested;

            ReportHotkeyRegistration(
                _hotkeyManager.IsToggleRegistered,
                "Home starts or stops automation.",
                "The Home hotkey is unavailable. Close any legacy Idle Runner instance and restart this application.",
                _hotkeyManager.ToggleRegistrationError);
            ReportHotkeyRegistration(
                _hotkeyManager.IsCloseRegistered,
                "Shift+Escape exits.",
                "The Shift+Escape hotkey is unavailable.",
                _hotkeyManager.CloseRegistrationError);
            ReportHotkeyRegistration(
                _hotkeyManager.IsAutoUpgradeRegistered,
                "Ctrl+Shift+B runs auto upgrade now.",
                "The Ctrl+Shift+B auto-upgrade hotkey is unavailable.",
                _hotkeyManager.AutoUpgradeRegistrationError);
        }
        catch (Exception exception)
        {
            _viewModel.ReportError("Global hotkeys could not be registered.", exception);
        }
    }

    private async void OnPrimaryActionClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.ToggleAutomationAsync();
    }

    private async void OnCalibrateGameplayClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.CalibrateGameplayAsync();
    }

    private async void OnCalibrateSilverBoxClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.CalibrateSilverBoxAsync();
    }

    private async void OnCalibrateChestHuntClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.CalibrateChestHuntAsync();
    }

    private async void OnHotkeyToggleRequested(object? sender, EventArgs e)
    {
        await _viewModel.ToggleAutomationAsync();
    }

    private void OnHotkeyCloseRequested(object? sender, EventArgs e)
    {
        Close();
    }

    private async void OnHotkeyAutoUpgradeRequested(object? sender, EventArgs e)
    {
        await _viewModel.RunAutoUpgradeNowAsync();
    }

    private void OnCopyActivityClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_viewModel.ActivityText))
        {
            return;
        }

        try
        {
            Clipboard.SetText(_viewModel.ActivityText);
            _viewModel.ReportInfo("The activity log was copied to the clipboard.");
        }
        catch (Exception exception)
        {
            _viewModel.ReportError("The activity log could not be copied.", exception);
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_shutdownCompleted)
        {
            return;
        }

        e.Cancel = true;
        IsEnabled = false;
        DisposeHotkeys();
        await _viewModel.ShutdownAsync();
        _shutdownCompleted = true;
        Close();
    }

    private void DisposeHotkeys()
    {
        if (_hotkeyManager is null)
        {
            return;
        }

        _hotkeyManager.ToggleRequested -= OnHotkeyToggleRequested;
        _hotkeyManager.CloseRequested -= OnHotkeyCloseRequested;
        _hotkeyManager.AutoUpgradeRequested -= OnHotkeyAutoUpgradeRequested;
        _hotkeyManager.Dispose();
        _hotkeyManager = null;
    }

    private void ReportHotkeyRegistration(
        bool isRegistered,
        string successMessage,
        string failureMessage,
        Exception? exception)
    {
        if (isRegistered)
        {
            _viewModel.ReportInfo($"Global hotkey registered: {successMessage}");
            return;
        }

        _viewModel.ReportError(
            failureMessage,
            exception ?? new InvalidOperationException("The global hotkey registration failed."));
    }
}
