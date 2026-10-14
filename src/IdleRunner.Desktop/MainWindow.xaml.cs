using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;

namespace IdleRunner.Desktop;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
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

    private void OnPrimaryActionClick(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleAutomation();
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_shutdownCompleted)
        {
            return;
        }

        e.Cancel = true;
        IsEnabled = false;
        await _viewModel.ShutdownAsync();
        _shutdownCompleted = true;
        Close();
    }
}
