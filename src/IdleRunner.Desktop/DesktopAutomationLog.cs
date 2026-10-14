using System.IO;
using IdleRunner.Core;

namespace IdleRunner.Desktop;

public sealed class DesktopAutomationLog : IAutomationLog
{
    private readonly object _writeLock = new();
    private readonly string _logPath;

    public DesktopAutomationLog()
    {
        string logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IdleRunner",
            "logs");

        Directory.CreateDirectory(logDirectory);
        _logPath = Path.Combine(logDirectory, $"idle-runner-{DateTime.UtcNow:yyyyMMdd}.log");
    }

    public event EventHandler<string>? EntryWritten;

    public void Info(string message)
    {
        Write("INFO", message);
    }

    public void Error(string message, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Write("ERROR", $"{message} {exception.GetType().Name}: {exception.Message}");
    }

    private void Write(string level, string message)
    {
        string entry = $"{DateTimeOffset.Now:HH:mm:ss}  {level,-5}  {message}";

        lock (_writeLock)
        {
            File.AppendAllText(_logPath, entry + Environment.NewLine);
        }

        EntryWritten?.Invoke(this, entry);
    }
}
