namespace IdleRunner.Core;

public sealed class VisualObservationDefinition
{
    private readonly IReadOnlyList<VisualAnchorDefinition> _anchors;

    public VisualObservationDefinition(
        string name,
        IEnumerable<VisualAnchorDefinition> anchors,
        int? requiredMatches = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("An observation name is required.", nameof(name));
        }

        if (anchors is null)
        {
            throw new ArgumentNullException(nameof(anchors));
        }

        VisualAnchorDefinition[] anchorArray = anchors.ToArray();
        if (anchorArray.Length == 0)
        {
            throw new ArgumentException("At least one visual anchor is required.", nameof(anchors));
        }

        int normalizedRequiredMatches = requiredMatches ?? anchorArray.Length;
        if (normalizedRequiredMatches <= 0 || normalizedRequiredMatches > anchorArray.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredMatches));
        }

        Name = name;
        _anchors = anchorArray;
        RequiredMatches = normalizedRequiredMatches;
    }

    public string Name { get; }

    public IReadOnlyList<VisualAnchorDefinition> Anchors => _anchors;

    public int RequiredMatches { get; }

    public VisualObservationResult Evaluate(GameFrame frame)
    {
        if (frame is null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        VisualAnchorResult[] results = new VisualAnchorResult[_anchors.Count];
        int matchedCount = 0;
        double totalScore = 0;

        for (int index = 0; index < _anchors.Count; index++)
        {
            VisualAnchorResult result = _anchors[index].Evaluate(frame);
            results[index] = result;
            totalScore += result.Match.Score;

            if (result.IsMatch)
            {
                matchedCount++;
            }
        }

        return new VisualObservationResult(
            Name,
            results,
            matchedCount >= RequiredMatches,
            totalScore / results.Length);
    }
}
