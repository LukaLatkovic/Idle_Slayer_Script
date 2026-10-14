namespace IdleRunner.Core;

public sealed class VisualAnchorDefinition
{
    public VisualAnchorDefinition(
        string name,
        LuminanceTemplate template,
        FrameRectangle searchRegion,
        double minimumScore,
        int searchStep = 1)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("An anchor name is required.", nameof(name));
        }

        if (template is null)
        {
            throw new ArgumentNullException(nameof(template));
        }

        if (minimumScore < 0 || minimumScore > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumScore));
        }

        if (searchStep <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(searchStep));
        }

        Name = name;
        Template = template;
        SearchRegion = searchRegion;
        MinimumScore = minimumScore;
        SearchStep = searchStep;
    }

    public string Name { get; }

    public LuminanceTemplate Template { get; }

    public FrameRectangle SearchRegion { get; }

    public double MinimumScore { get; }

    public int SearchStep { get; }

    public VisualAnchorResult Evaluate(GameFrame frame)
    {
        TemplateMatchResult match = LuminanceTemplateMatcher.FindBest(
            frame,
            SearchRegion,
            Template,
            SearchStep);
        return new VisualAnchorResult(Name, match, match.Score >= MinimumScore);
    }
}
