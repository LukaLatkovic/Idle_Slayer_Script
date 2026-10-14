namespace IdleRunner.Core;

public readonly struct FrameRectangle
{
    public FrameRectangle(int left, int top, int width, int height)
    {
        if (left < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(left));
        }

        if (top < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(top));
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }

    public int Left { get; }

    public int Top { get; }

    public int Width { get; }

    public int Height { get; }

    public int Right => checked(Left + Width);

    public int Bottom => checked(Top + Height);

    public bool FitsWithin(int width, int height)
    {
        return Left <= width
            && Top <= height
            && Right <= width
            && Bottom <= height;
    }
}
