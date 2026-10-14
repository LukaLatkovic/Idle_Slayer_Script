namespace IdleRunner.Core;

public readonly struct ChestHuntGridPosition : IEquatable<ChestHuntGridPosition>
{
    public ChestHuntGridPosition(int row, int column, int centerX, int centerY)
    {
        if (row is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        if (column is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        Row = row;
        Column = column;
        CenterX = centerX;
        CenterY = centerY;
    }

    public int Row { get; }

    public int Column { get; }

    public int CenterX { get; }

    public int CenterY { get; }

    public bool Equals(ChestHuntGridPosition other)
    {
        return Row == other.Row && Column == other.Column;
    }

    public override bool Equals(object? obj)
    {
        return obj is ChestHuntGridPosition other && Equals(other);
    }

    public override int GetHashCode()
    {
        return (Row * 397) ^ Column;
    }

    public static bool operator ==(ChestHuntGridPosition left, ChestHuntGridPosition right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ChestHuntGridPosition left, ChestHuntGridPosition right)
    {
        return !left.Equals(right);
    }
}
