using System.IO;
using System.Text.Json;
using IdleRunner.Core;

namespace IdleRunner.Desktop;

public sealed class SilverBoxCalibrationStore
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _calibrationPath;

    public SilverBoxCalibrationStore()
    {
        string applicationDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IdleRunner");

        _calibrationPath = Path.Combine(applicationDirectory, "silver-box-calibration.json");
    }

    public SilverBoxCalibration? Load()
    {
        try
        {
            if (!File.Exists(_calibrationPath))
            {
                return null;
            }

            string json = File.ReadAllText(_calibrationPath);
            CalibrationDocument? document = JsonSerializer.Deserialize<CalibrationDocument>(json);
            if (document is null || document.SchemaVersion != SchemaVersion)
            {
                return null;
            }

            byte[] luminance = Convert.FromBase64String(document.Icon);
            return new SilverBoxCalibration(
                LuminanceTemplate.FromLuminance(
                    document.TemplateWidth,
                    document.TemplateHeight,
                    luminance));
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

    public void Save(SilverBoxCalibration calibration)
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
            TemplateWidth = calibration.Icon.Width,
            TemplateHeight = calibration.Icon.Height,
            Icon = Convert.ToBase64String(calibration.Icon.ToLuminanceArray())
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

        public string Icon { get; set; } = string.Empty;
    }
}
