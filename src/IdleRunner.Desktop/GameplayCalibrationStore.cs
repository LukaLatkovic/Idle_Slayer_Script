using System.IO;
using System.Text.Json;
using IdleRunner.Core;

namespace IdleRunner.Desktop;

public sealed class GameplayCalibrationStore
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _calibrationPath;

    public GameplayCalibrationStore()
    {
        string applicationDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IdleRunner");

        _calibrationPath = Path.Combine(applicationDirectory, "gameplay-calibration.json");
    }

    public GameplayHudCalibration? Load()
    {
        try
        {
            if (!File.Exists(_calibrationPath))
            {
                return null;
            }

            string json = File.ReadAllText(_calibrationPath);
            CalibrationDocument? document = JsonSerializer.Deserialize<CalibrationDocument>(json);

            if (document is null
                || document.SchemaVersion != SchemaVersion
                || document.TemplateWidth <= 0
                || document.TemplateHeight <= 0)
            {
                return null;
            }

            byte[] left = Convert.FromBase64String(document.LeftActionSlot);
            byte[] right = Convert.FromBase64String(document.RightActionSlot);

            return new GameplayHudCalibration(
                LuminanceTemplate.FromLuminance(document.TemplateWidth, document.TemplateHeight, left),
                LuminanceTemplate.FromLuminance(document.TemplateWidth, document.TemplateHeight, right));
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

    public void Save(GameplayHudCalibration calibration)
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
            TemplateWidth = calibration.LeftActionSlot.Width,
            TemplateHeight = calibration.LeftActionSlot.Height,
            LeftActionSlot = Convert.ToBase64String(calibration.LeftActionSlot.ToLuminanceArray()),
            RightActionSlot = Convert.ToBase64String(calibration.RightActionSlot.ToLuminanceArray())
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

        public string LeftActionSlot { get; set; } = string.Empty;

        public string RightActionSlot { get; set; } = string.Empty;
    }
}
