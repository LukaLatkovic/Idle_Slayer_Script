namespace IdleRunner.Core;

public sealed class AutomationSettings
{
    public const int CurrentSchemaVersion = 2;
    public const int MinimumIntervalMilliseconds = 25;
    public const int MaximumIntervalMilliseconds = 2_000;
    public const int MinimumTimerMinutes = 1;
    public const int MaximumTimerMinutes = 10_080;

    public int SettingsSchemaVersion { get; set; } = CurrentSchemaVersion;

    public int JumpIntervalMilliseconds { get; set; } = 150;

    public bool AutoBuyUpgradesEnabled { get; set; }

    public int AutoBuyIntervalMinutes { get; set; } = 10;

    public bool AutoAscendEnabled { get; set; }

    public int AutoAscendIntervalMinutes { get; set; } = 10;

    public bool CirclePortalsEnabled { get; set; }

    public int PortalCyclePosition { get; set; } = 7;

    public bool DisableRageWithoutSoulBonus { get; set; }

    public bool SkipBonusStage { get; set; }

    public bool NoLockpicking { get; set; } = true;

    public bool NoReinforcedCrystalSaver { get; set; }

    public bool PerfectChestHunt { get; set; }

    public bool CraftSoulsCompass { get; set; }

    public bool CraftRagePill { get; set; }

    public bool CraftDimensionalStaff { get; set; }

    public bool CraftBidimensionalStaff { get; set; }

    public int RequiredClientWidth { get; set; } = 1_280;

    public int RequiredClientHeight { get; set; } = 720;

    public AutomationSettings Normalize()
    {
        return new AutomationSettings
        {
            SettingsSchemaVersion = CurrentSchemaVersion,
            JumpIntervalMilliseconds = Math.Max(
                MinimumIntervalMilliseconds,
                Math.Min(MaximumIntervalMilliseconds, JumpIntervalMilliseconds)),
            AutoBuyUpgradesEnabled = AutoBuyUpgradesEnabled,
            AutoBuyIntervalMinutes = NormalizeTimerMinutes(AutoBuyIntervalMinutes),
            AutoAscendEnabled = AutoAscendEnabled,
            AutoAscendIntervalMinutes = NormalizeTimerMinutes(AutoAscendIntervalMinutes),
            CirclePortalsEnabled = CirclePortalsEnabled,
            PortalCyclePosition = Math.Max(1, Math.Min(9, PortalCyclePosition)),
            DisableRageWithoutSoulBonus = DisableRageWithoutSoulBonus,
            SkipBonusStage = SkipBonusStage,
            NoLockpicking = NoLockpicking,
            NoReinforcedCrystalSaver = NoReinforcedCrystalSaver,
            PerfectChestHunt = PerfectChestHunt,
            CraftSoulsCompass = CraftSoulsCompass,
            CraftRagePill = CraftRagePill,
            CraftDimensionalStaff = CraftDimensionalStaff,
            CraftBidimensionalStaff = CraftBidimensionalStaff,
            RequiredClientWidth = Math.Max(1, RequiredClientWidth),
            RequiredClientHeight = Math.Max(1, RequiredClientHeight)
        };
    }

    private static int NormalizeTimerMinutes(int value)
    {
        return Math.Max(MinimumTimerMinutes, Math.Min(MaximumTimerMinutes, value));
    }
}
