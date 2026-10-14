namespace IdleRunner.Core;

public readonly struct TemplateMatchResult
{
    public TemplateMatchResult(int left, int top, double score)
    {
        Left = left;
        Top = top;
        Score = Math.Max(0, Math.Min(1, score));
    }

    public int Left { get; }

    public int Top { get; }

    public double Score { get; }
}
