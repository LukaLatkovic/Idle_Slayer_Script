namespace IdleRunner.Core;

public sealed class SilverBoxCalibration
{
    public const int RequiredWidth = 1_280;
    public const int RequiredHeight = 720;
    // AutoIt clicked screen position (644, 49), which included the native
    // window frame. The Windows adapter uses client coordinates instead.
    public const int ClickX = 640;
    public const int ClickY = 17;

    private static readonly FrameRectangle TemplateRegion = new(631, 7, 20, 20);
    private static readonly FrameRectangle SearchRegion = new(600, 0, 80, 50);

    public SilverBoxCalibration(LuminanceTemplate icon)
    {
        Icon = icon ?? throw new ArgumentNullException(nameof(icon));

        if (icon.Width != TemplateRegion.Width || icon.Height != TemplateRegion.Height)
        {
            throw new ArgumentException("The Silver Box template has unexpected dimensions.", nameof(icon));
        }

        ValidateIconSignature(icon);
    }

    public LuminanceTemplate Icon { get; }

    public static SilverBoxCalibration FromFrame(GameFrame frame)
    {
        if (frame is null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        if (frame.Width != RequiredWidth || frame.Height != RequiredHeight)
        {
            throw new ArgumentException(
                $"Silver Box calibration requires a {RequiredWidth} x {RequiredHeight} frame.",
                nameof(frame));
        }

        return new SilverBoxCalibration(LuminanceTemplate.FromFrame(frame, TemplateRegion));
    }

    public VisualObservationDefinition CreateObservation()
    {
        return new VisualObservationDefinition(
            "Silver Box claim icon",
            [
                new VisualAnchorDefinition(
                    "Silver Box icon",
                    Icon,
                    SearchRegion,
                    minimumScore: 0.92)
            ]);
    }

    private static void ValidateIconSignature(LuminanceTemplate icon)
    {
        byte[] luminance = icon.ToLuminanceArray();
        bool[] observedLevels = new bool[256];
        int distinctLevelCount = 0;
        int brightPixelCount = 0;
        int darkPixelCount = 0;

        foreach (byte value in luminance)
        {
            if (!observedLevels[value])
            {
                observedLevels[value] = true;
                distinctLevelCount++;
            }

            if (value >= 240)
            {
                brightPixelCount++;
            }

            if (value <= 30)
            {
                darkPixelCount++;
            }
        }

        if (distinctLevelCount < 4
            || brightPixelCount < luminance.Length / 10
            || darkPixelCount < luminance.Length / 20)
        {
            throw new ArgumentException(
                "The Silver Box claim icon is not visible in its expected top-center position.",
                nameof(icon));
        }
    }
}
