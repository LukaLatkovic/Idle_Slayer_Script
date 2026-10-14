using System.IO;
using System.Text.Json;
using IdleRunner.Core;

namespace IdleRunner.Desktop;

public sealed class ChestHuntCalibrationStore
{
    private const int SchemaVersion = 2;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _calibrationPath;

    public ChestHuntCalibrationStore()
    {
        string applicationDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IdleRunner");

        _calibrationPath = Path.Combine(applicationDirectory, "chest-hunt-calibration.json");
    }

    public ChestHuntCalibration? Load()
    {
        try
        {
            if (!File.Exists(_calibrationPath))
            {
                return null;
            }

            CalibrationDocument? document = JsonSerializer.Deserialize<CalibrationDocument>(
                File.ReadAllText(_calibrationPath));
            if (document is null || document.SchemaVersion != SchemaVersion)
            {
                return null;
            }

            return new ChestHuntCalibration(
                LuminanceTemplate.FromLuminance(
                    document.TemplateWidth,
                    document.TemplateHeight,
                    Convert.FromBase64String(document.SaverTemplate)),
                LuminanceTemplate.FromLuminance(
                    document.ClosedChestTemplateWidth,
                    document.ClosedChestTemplateHeight,
                    Convert.FromBase64String(document.ClosedChestTemplate)));
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or FormatException
            or ArgumentException
            or OverflowException)
        {
            return null;
        }
    }

    public void Save(ChestHuntCalibration calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);

        string? directory = Path.GetDirectoryName(_calibrationPath);
        if (directory is null)
        {
            throw new InvalidOperationException("The calibration directory could not be resolved.");
        }

        Directory.CreateDirectory(directory);
        CalibrationDocument document = new()
        {
            SchemaVersion = SchemaVersion,
            TemplateWidth = calibration.SaverTemplate.Width,
            TemplateHeight = calibration.SaverTemplate.Height,
            SaverTemplate = Convert.ToBase64String(calibration.SaverTemplate.ToLuminanceArray()),
            ClosedChestTemplateWidth = calibration.ClosedChestTemplate.Width,
            ClosedChestTemplateHeight = calibration.ClosedChestTemplate.Height,
            ClosedChestTemplate = Convert.ToBase64String(
                calibration.ClosedChestTemplate.ToLuminanceArray())
        };

        File.WriteAllText(
            _calibrationPath,
            JsonSerializer.Serialize(document, SerializerOptions));
    }

    private sealed class CalibrationDocument
    {
        public int SchemaVersion { get; set; }

        public int TemplateWidth { get; set; }

        public int TemplateHeight { get; set; }

        public string SaverTemplate { get; set; } = string.Empty;

        public int ClosedChestTemplateWidth { get; set; }

        public int ClosedChestTemplateHeight { get; set; }

        public string ClosedChestTemplate { get; set; } = string.Empty;
    }
}
