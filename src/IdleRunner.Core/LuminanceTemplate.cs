namespace IdleRunner.Core;

public sealed class LuminanceTemplate
{
    private readonly byte[] _luminance;

    private LuminanceTemplate(int width, int height, byte[] luminance)
    {
        Width = width;
        Height = height;
        _luminance = luminance;
    }

    public int Width { get; }

    public int Height { get; }

    public static LuminanceTemplate FromFrame(GameFrame frame, FrameRectangle region)
    {
        if (frame is null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        if (!region.FitsWithin(frame.Width, frame.Height))
        {
            throw new ArgumentOutOfRangeException(nameof(region), "The template region must fit inside the frame.");
        }

        byte[] luminance = new byte[checked(region.Width * region.Height)];
        int targetIndex = 0;

        for (int y = region.Top; y < region.Bottom; y++)
        {
            for (int x = region.Left; x < region.Right; x++)
            {
                luminance[targetIndex++] = frame.GetLuminance(x, y);
            }
        }

        EnsureTemplateHasContrast(luminance);
        return new LuminanceTemplate(region.Width, region.Height, luminance);
    }

    public static LuminanceTemplate FromLuminance(int width, int height, byte[] luminance)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (luminance is null)
        {
            throw new ArgumentNullException(nameof(luminance));
        }

        if (luminance.Length != checked(width * height))
        {
            throw new ArgumentException("The luminance buffer has an unexpected length.", nameof(luminance));
        }

        byte[] copy = (byte[])luminance.Clone();
        EnsureTemplateHasContrast(copy);
        return new LuminanceTemplate(width, height, copy);
    }

    public byte[] ToLuminanceArray()
    {
        return (byte[])_luminance.Clone();
    }

    internal byte GetLuminance(int x, int y)
    {
        return _luminance[checked(y * Width + x)];
    }

    private static void EnsureTemplateHasContrast(byte[] luminance)
    {
        byte first = luminance[0];
        for (int index = 1; index < luminance.Length; index++)
        {
            if (luminance[index] != first)
            {
                return;
            }
        }

        throw new ArgumentException("A luminance template must contain visible contrast.", nameof(luminance));
    }
}
