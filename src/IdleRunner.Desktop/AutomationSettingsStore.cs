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

    public AutomationSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AutomationSettings();
            }

            string json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AutomationSettings>(json) ?? new AutomationSettings();
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
