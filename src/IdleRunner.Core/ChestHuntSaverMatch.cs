namespace IdleRunner.Core;

public sealed class ChestHuntSaverMatch
{
    public ChestHuntSaverMatch(ChestHuntGridPosition position, double score, bool isMatch)
    {
        Position = position;
        Score = Math.Max(0, Math.Min(1, score));
        IsMatch = isMatch;
    }

    public ChestHuntGridPosition Position { get; }

    public double Score { get; }

    public bool IsMatch { get; }
}
