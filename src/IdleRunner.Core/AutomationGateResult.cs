namespace IdleRunner.Core;

public sealed class AutomationGateResult
{
    private AutomationGateResult(bool allowsActions, string description)
    {
        AllowsActions = allowsActions;
        Description = description;
    }

    public bool AllowsActions { get; }

    public string Description { get; }

    public static AutomationGateResult Allowed(string description = "Visual safety gate is ready.")
    {
        return new AutomationGateResult(true, description);
    }

    public static AutomationGateResult Blocked(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("A blocked gate requires a description.", nameof(description));
        }

        return new AutomationGateResult(false, description);
    }
}
