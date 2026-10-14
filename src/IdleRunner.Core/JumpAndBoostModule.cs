namespace IdleRunner.Core;

public sealed class JumpAndBoostModule : IAutomationModule
{
    public string Name => "Jump and boost";

    public AutomationAction Action => AutomationAction.JumpAndBoost;

    public TimeSpan GetInterval(AutomationSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        return TimeSpan.FromMilliseconds(settings.JumpIntervalMilliseconds);
    }

    public bool CanRun(GameSnapshot snapshot, AutomationSettings settings)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        return snapshot.CanReceiveInput(settings);
    }
}
