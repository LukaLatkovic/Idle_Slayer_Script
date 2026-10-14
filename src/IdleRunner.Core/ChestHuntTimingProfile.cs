namespace IdleRunner.Core;

public sealed class ChestHuntTimingProfile
{
    private ChestHuntTimingProfile(
        TimeSpan initialDelay,
        TimeSpan chestRevealDelay,
        TimeSpan mimicRevealDelay)
    {
        InitialDelay = initialDelay;
        ChestRevealDelay = chestRevealDelay;
        MimicRevealDelay = mimicRevealDelay;
    }

    public TimeSpan InitialDelay { get; }

    public TimeSpan ChestRevealDelay { get; }

    public TimeSpan MimicRevealDelay { get; }

    public static ChestHuntTimingProfile FromSettings(AutomationSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        return settings.NoLockpicking
            ? new ChestHuntTimingProfile(
                TimeSpan.FromSeconds(4),
                TimeSpan.FromMilliseconds(1_500),
                TimeSpan.FromMilliseconds(2_500))
            : new ChestHuntTimingProfile(
                TimeSpan.FromSeconds(2),
                TimeSpan.FromMilliseconds(550),
                TimeSpan.FromMilliseconds(1_500));
    }
}
