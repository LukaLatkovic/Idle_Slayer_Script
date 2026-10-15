namespace IdleRunner.Core;

public static class BonusStageVisualDetector
{
    private const int DefaultTolerance = 8;

    public static bool IsEntranceVisible(GameFrame frame)
    {
        return ContainsColor(frame, 630, 224, 680, 274, 0xFF, 0xE7, 0x37, 28)
            && ContainsColor(frame, 608, 206, 668, 266, 0xFF, 0xBB, 0x31, 28);
    }

    public static bool IsStage3(GameFrame frame) =>
        ContainsColor(frame, 160, 498, 200, 498, 0x16, 0x17, 0x1F, 8);

    public static bool HasSpiritBoost(GameFrame frame) =>
        ContainsColor(frame, 440, 78, 468, 104, 0xE1, 0xE0, 0xE2, 20);

    public static bool IsSliderVisible(GameFrame frame) =>
        ContainsColor(frame, 765, 438, 785, 458, 0xFF, 0xFF, 0xFF, 12);

    public static bool IsFailureVisible(GameFrame frame) =>
        IsColor(frame, 716, 600, 0xAF, 0x00, 0x00);

    public static bool IsRetryVisible(GameFrame frame) =>
        IsColor(frame, 615, 590, 0x00, 0xA4, 0x00);

    public static bool IsStage3FailureVisible(GameFrame frame)
    {
        return IsRetryVisible(frame)
            || IsColor(frame, 1130, 604, 0x98, 0x98, 0x98)
            || ContainsColor(frame, 115, 570, 125, 590, 0x09, 0x43, 0x9B)
            || ContainsColor(frame, 115, 570, 125, 590, 0x09, 0x9B, 0x66);
    }

    public static bool ContainsColor(
        GameFrame frame,
        int left,
        int top,
        int right,
        int bottom,
        byte red,
        byte green,
        byte blue,
        int tolerance = DefaultTolerance)
    {
        if (frame is null)
        {
            throw new ArgumentNullException(nameof(frame));
        }
        int firstX = Math.Max(0, Math.Min(left, right));
        int lastX = Math.Min(frame.Width - 1, Math.Max(left, right));
        int firstY = Math.Max(0, Math.Min(top, bottom));
        int lastY = Math.Min(frame.Height - 1, Math.Max(top, bottom));

        for (int y = firstY; y <= lastY; y++)
        {
            for (int x = firstX; x <= lastX; x++)
            {
                if (Matches(frame.GetPixel(x, y), red, green, blue, tolerance))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsColor(
        GameFrame frame,
        int x,
        int y,
        byte red,
        byte green,
        byte blue) =>
        x >= 0 && y >= 0 && x < frame.Width && y < frame.Height
        && Matches(frame.GetPixel(x, y), red, green, blue, DefaultTolerance);

    private static bool Matches(
        RgbColor actual,
        byte red,
        byte green,
        byte blue,
        int tolerance) =>
        Math.Abs(actual.Red - red) <= tolerance
        && Math.Abs(actual.Green - green) <= tolerance
        && Math.Abs(actual.Blue - blue) <= tolerance;
}
