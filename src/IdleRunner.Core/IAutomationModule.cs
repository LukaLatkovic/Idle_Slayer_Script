namespace IdleRunner.Core;

public interface IAutomationModule
{
    string Name { get; }

    AutomationAction Action { get; }

    TimeSpan GetInterval(AutomationSettings settings);

    bool CanRun(GameSnapshot snapshot, AutomationSettings settings);
}
