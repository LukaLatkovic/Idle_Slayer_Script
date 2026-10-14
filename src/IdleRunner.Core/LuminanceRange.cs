namespace IdleRunner.Core;

public readonly struct LuminanceRange
{
    public LuminanceRange(byte darkest, byte lightest)
    {
        if (darkest > lightest)
        {
            throw new ArgumentOutOfRangeException(nameof(darkest));
        }

        Darkest = darkest;
        Lightest = lightest;
    }

    public byte Darkest { get; }

    public byte Lightest { get; }

    public int Span => Lightest - Darkest;
}
