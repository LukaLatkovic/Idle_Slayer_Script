namespace IdleRunner.Core;

public interface IObservedAutomationModule : IAutomationModule
{
    Task<bool> CanRunAsync(
        GameSnapshot snapshot,
        AutomationSettings settings,
        CancellationToken cancellationToken);
}
