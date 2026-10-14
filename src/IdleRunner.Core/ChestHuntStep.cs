namespace IdleRunner.Core;

public sealed class ChestHuntStep
{
    public ChestHuntStep(
        ChestHuntStrategyState nextState,
        ChestHuntAction action,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A Chest Hunt decision reason is required.", nameof(reason));
        }

        NextState = nextState;
        Action = action;
        Reason = reason;
    }

    public ChestHuntStrategyState NextState { get; }

    public ChestHuntAction Action { get; }

    public string Reason { get; }
}
