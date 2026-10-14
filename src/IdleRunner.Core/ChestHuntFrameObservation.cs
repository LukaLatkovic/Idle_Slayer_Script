namespace IdleRunner.Core;

public sealed class ChestHuntFrameObservation
{
    public ChestHuntFrameObservation(
        ChestHuntSaverMatch saver,
        IReadOnlyList<ChestHuntGridPosition> closedPositions)
    {
        Saver = saver ?? throw new ArgumentNullException(nameof(saver));
        ClosedPositions = closedPositions ?? throw new ArgumentNullException(nameof(closedPositions));
    }

    public ChestHuntSaverMatch Saver { get; }

    public IReadOnlyList<ChestHuntGridPosition> ClosedPositions { get; }

    public bool IsActive => Saver.IsMatch && ClosedPositions.Count >= 5;

    public bool IsFresh
    {
        get
        {
            if (!IsActive)
            {
                return false;
            }

            return ChestHuntCalibration.Positions
                .Where(position => position != Saver.Position)
                .All(ClosedPositions.Contains);
        }
    }

    public int OpenedNonSaverCount => IsActive
        ? ChestHuntCalibration.Positions.Count - 1
            - ClosedPositions.Count(position => position != Saver.Position)
        : 0;
}
