namespace IdleRunner.Core;

public sealed class VisualObservationResult
{
    public VisualObservationResult(
        string name,
        IReadOnlyList<VisualAnchorResult> anchors,
        bool isMatch,
        double confidence)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Anchors = anchors ?? throw new ArgumentNullException(nameof(anchors));
        IsMatch = isMatch;
        Confidence = Math.Max(0, Math.Min(1, confidence));
    }

    public string Name { get; }

    public IReadOnlyList<VisualAnchorResult> Anchors { get; }

    public bool IsMatch { get; }

    public double Confidence { get; }
}
