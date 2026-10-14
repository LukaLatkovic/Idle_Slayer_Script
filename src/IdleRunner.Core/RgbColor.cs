namespace IdleRunner.Core;

public readonly struct RgbColor : IEquatable<RgbColor>
{
    public RgbColor(byte red, byte green, byte blue)
    {
        Red = red;
        Green = green;
        Blue = blue;
    }

    public byte Red { get; }

    public byte Green { get; }

    public byte Blue { get; }

    public bool Equals(RgbColor other)
    {
        return Red == other.Red && Green == other.Green && Blue == other.Blue;
    }

    public override bool Equals(object? obj)
    {
        return obj is RgbColor other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = Red;
            hash = (hash * 397) ^ Green;
            return (hash * 397) ^ Blue;
        }
    }

    public static bool operator ==(RgbColor left, RgbColor right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(RgbColor left, RgbColor right)
    {
        return !left.Equals(right);
    }
}
