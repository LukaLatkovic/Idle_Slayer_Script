using System.IO;
using System.Text.Json;
using IdleRunner.Core;

namespace IdleRunner.Desktop;

public sealed class AutomationSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public AutomationSettingsStore()
    {
        string applicationDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IdleRunner");

        _settingsPath = Path.Combine(applicationDirectory, "settings.json");
    }

    public string? ImportedLegacySettingsPath { get; private set; }

    public AutomationSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return LoadLegacySettingsOrDefault();
            }

            string json = File.ReadAllText(_settingsPath);
            AutomationSettings settings = JsonSerializer.Deserialize<AutomationSettings>(json)
                ?? new AutomationSettings();

            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(nameof(AutomationSettings.SettingsSchemaVersion), out _)
                && TryLoadLegacySettings(out AutomationSettings? legacySettings, out string? legacyPath))
            {
                legacySettings.RequiredClientWidth = settings.RequiredClientWidth;
                legacySettings.RequiredClientHeight = settings.RequiredClientHeight;
                ImportedLegacySettingsPath = legacyPath;
                Save(legacySettings);
                return legacySettings;
            }

            return settings.Normalize();
        }
        catch (IOException)
        {
            return new AutomationSettings();
        }
        catch (JsonException)
        {
            return new AutomationSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AutomationSettings();
        }
    }

    private AutomationSettings LoadLegacySettingsOrDefault()
    {
        if (!TryLoadLegacySettings(out AutomationSettings? settings, out string? legacyPath))
        {
            return new AutomationSettings();
        }

        ImportedLegacySettingsPath = legacyPath;
        Save(settings);
        return settings;
    }

    private static bool TryLoadLegacySettings(
        out AutomationSettings settings,
        out string? legacyPath)
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidates =
        [
            Path.Combine(Environment.CurrentDirectory, "IdleRunnerLogs", "Settings.txt"),
            Path.Combine(userProfile, "Downloads", "IdleRunnerLogs", "Settings.txt")
        ];

        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            settings = LegacyAutomationSettingsParser.Parse(File.ReadLines(candidate));
            legacyPath = candidate;
            return true;
        }

        settings = new AutomationSettings();
        legacyPath = null;
        return false;
    }

    public void Save(AutomationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string? directory = Path.GetDirectoryName(_settingsPath);
        if (directory is null)
        {
            return;
        }

        Directory.CreateDirectory(directory);
        string json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(_settingsPath, json);
    }
}
