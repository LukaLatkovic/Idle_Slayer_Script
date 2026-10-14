namespace IdleRunner.Core;

public interface IAutomationGate
{
    Task<AutomationGateResult> CheckAsync(
        GameSnapshot snapshot,
        AutomationSettings settings,
        CancellationToken cancellationToken);
}
