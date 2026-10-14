using System.Threading;
using System.Windows;

namespace IdleRunner.Desktop;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            name: "Local\\IdleRunner.Desktop.SingleInstance",
            createdNew: out _ownsMutex);

        if (!_ownsMutex)
        {
            MessageBox.Show(
                "Idle Runner is already running.",
                "Idle Runner",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        MainWindow window = new();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
