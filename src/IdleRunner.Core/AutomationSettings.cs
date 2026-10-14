namespace IdleRunner.Core;

public sealed class AutomationSettings
{
    public const int MinimumIntervalMilliseconds = 25;
    public const int MaximumIntervalMilliseconds = 2_000;

    public int JumpIntervalMilliseconds { get; set; } = 150;

    public int RequiredClientWidth { get; set; } = 1_280;

    public int RequiredClientHeight { get; set; } = 720;

    public AutomationSettings Normalize()
    {
        return new AutomationSettings
        {
            JumpIntervalMilliseconds = Math.Max(
                MinimumIntervalMilliseconds,
                Math.Min(MaximumIntervalMilliseconds, JumpIntervalMilliseconds)),
            RequiredClientWidth = Math.Max(1, RequiredClientWidth),
            RequiredClientHeight = Math.Max(1, RequiredClientHeight)
        };
    }
}
