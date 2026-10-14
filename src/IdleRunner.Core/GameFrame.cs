namespace IdleRunner.Core;

public sealed class GameFrame
{
    private const int BytesPerPixel = 4;
    private readonly byte[] _pixels;

    public GameFrame(int width, int height, byte[] bgra32Pixels)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (bgra32Pixels is null)
        {
            throw new ArgumentNullException(nameof(bgra32Pixels));
        }

        int expectedLength = checked(width * height * BytesPerPixel);
        if (bgra32Pixels.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Expected {expectedLength} BGRA bytes, received {bgra32Pixels.Length}.",
                nameof(bgra32Pixels));
        }

        Width = width;
        Height = height;
        Stride = checked(width * BytesPerPixel);
        _pixels = (byte[])bgra32Pixels.Clone();
    }

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }

    public ReadOnlyMemory<byte> Bgra32Pixels => _pixels;

    public RgbColor GetPixel(int x, int y)
    {
        int offset = GetPixelOffset(x, y);
        return new RgbColor(
            red: _pixels[offset + 2],
            green: _pixels[offset + 1],
            blue: _pixels[offset]);
    }

    public byte GetLuminance(int x, int y)
    {
        int offset = GetPixelOffset(x, y);
        int blue = _pixels[offset];
        int green = _pixels[offset + 1];
        int red = _pixels[offset + 2];
        return (byte)((77 * red + 150 * green + 29 * blue + 128) >> 8);
    }

    public LuminanceRange MeasureLuminanceRange(int sampleStep = 1)
    {
        if (sampleStep <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleStep));
        }

        byte darkest = byte.MaxValue;
        byte lightest = byte.MinValue;

        for (int y = 0; y < Height; y += sampleStep)
        {
            for (int x = 0; x < Width; x += sampleStep)
            {
                byte luminance = GetLuminance(x, y);
                darkest = Math.Min(darkest, luminance);
                lightest = Math.Max(lightest, luminance);
            }
        }

        return new LuminanceRange(darkest, lightest);
    }

    private int GetPixelOffset(int x, int y)
    {
        if ((uint)x >= (uint)Width)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if ((uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }

        return checked(y * Stride + x * BytesPerPixel);
    }
}
