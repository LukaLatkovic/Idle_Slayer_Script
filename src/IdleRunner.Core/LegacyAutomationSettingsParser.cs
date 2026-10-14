namespace IdleRunner.Core;

public static class LegacyAutomationSettingsParser
{
    public static AutomationSettings Parse(IEnumerable<string> lines)
    {
        if (lines is null)
        {
            throw new ArgumentNullException(nameof(lines));
        }

        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        bool isSettingsSection = false;

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("[", StringComparison.Ordinal)
                && line.EndsWith("]", StringComparison.Ordinal))
            {
                isSettingsSection = string.Equals(line, "[Settings]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!isSettingsSection)
            {
                continue;
            }

            int separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            string key = line.Substring(0, separatorIndex).Trim();
            string value = line.Substring(separatorIndex + 1).Trim();
            values[key] = value;
        }

        AutomationSettings settings = new();
        settings.AutoBuyIntervalMinutes = ReadInt(values, "iAutoBuyTimer", settings.AutoBuyIntervalMinutes);
        settings.AutoAscendIntervalMinutes = ReadInt(values, "iAutoAscendTimer", settings.AutoAscendIntervalMinutes);
        settings.AutoAscendEnabled = ReadBool(values, "bAutoAscendState", settings.AutoAscendEnabled);
        settings.AutoBuyUpgradesEnabled = ReadBool(values, "bAutoBuyUpgradeState", settings.AutoBuyUpgradesEnabled);
        settings.CraftSoulsCompass = ReadBool(values, "bCraftSoulBonusState", settings.CraftSoulsCompass);
        settings.SkipBonusStage = ReadBool(values, "bSkipBonusStageState", settings.SkipBonusStage);
        settings.CraftRagePill = ReadBool(values, "bCraftRagePillState", settings.CraftRagePill);
        settings.CirclePortalsEnabled = ReadBool(values, "bCirclePortalsState", settings.CirclePortalsEnabled);
        settings.JumpIntervalMilliseconds = ReadInt(values, "iJumpSliderValue", settings.JumpIntervalMilliseconds);
        settings.NoLockpicking = ReadBool(values, "bNoLockpickingState", settings.NoLockpicking);
        settings.PortalCyclePosition = ReadInt(values, "iCirclePortalsCount", settings.PortalCyclePosition);
        settings.CraftDimensionalStaff = ReadBool(values, "bDimensionalState", settings.CraftDimensionalStaff);
        settings.CraftBidimensionalStaff = ReadBool(values, "bBiDimensionalState", settings.CraftBidimensionalStaff);
        settings.DisableRageWithoutSoulBonus = ReadBool(values, "bDisableRageState", settings.DisableRageWithoutSoulBonus);
        settings.NoReinforcedCrystalSaver = ReadBool(
            values,
            "bNoReinforcedCrystalSaverState",
            settings.NoReinforcedCrystalSaver);
        settings.PerfectChestHunt = ReadBool(values, "bPerfectChestHuntState", settings.PerfectChestHunt);
        return settings.Normalize();
    }

    private static int ReadInt(Dictionary<string, string> values, string key, int defaultValue)
    {
        return values.TryGetValue(key, out string? value)
            && int.TryParse(value, out int parsed)
                ? parsed
                : defaultValue;
    }

    private static bool ReadBool(Dictionary<string, string> values, string key, bool defaultValue)
    {
        return values.TryGetValue(key, out string? value)
            && bool.TryParse(value, out bool parsed)
                ? parsed
                : defaultValue;
    }
}
