namespace IdleRunner.Core;

public interface IAutomationLog
{
    void Info(string message);

    void Error(string message, Exception exception);
}
