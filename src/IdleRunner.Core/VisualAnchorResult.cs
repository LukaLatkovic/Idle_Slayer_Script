namespace IdleRunner.Core;

public sealed class VisualAnchorResult
{
    public VisualAnchorResult(string name, TemplateMatchResult match, bool isMatch)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Match = match;
        IsMatch = isMatch;
    }

    public string Name { get; }

    public TemplateMatchResult Match { get; }

    public bool IsMatch { get; }
}
