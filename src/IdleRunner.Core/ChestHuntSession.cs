namespace IdleRunner.Core;

public sealed class ChestHuntSession
{
    private readonly Queue<ChestHuntGridPosition> _remainingChests;
    private ChestHuntStrategyState _strategyState = ChestHuntStrategyState.NoMimic;
    private bool _openingLifeSaver;
    private bool _isComplete;
    private int _openedChestIndex;

    public ChestHuntSession(ChestHuntGridPosition saverPosition)
    {
        SaverPosition = saverPosition;
        _remainingChests = new Queue<ChestHuntGridPosition>(
            ChestHuntCalibration.Positions.Where(position => position != saverPosition));
    }

    public ChestHuntGridPosition SaverPosition { get; }

    public ChestHuntGridPosition? NextPosition
    {
        get
        {
            if (_isComplete)
            {
                return null;
            }

            if (_openingLifeSaver)
            {
                return SaverPosition;
            }

            return _remainingChests.Count == 0 ? null : _remainingChests.Peek();
        }
    }

    public bool IsOpeningLifeSaver => _openingLifeSaver;

    public bool IsComplete => _isComplete || NextPosition is null;

    public int OpenedChestCount => _openedChestIndex;

    public ChestHuntStep RecordOutcome(
        ChestHuntOutcome outcome,
        AutomationSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (_isComplete || NextPosition is null)
        {
            throw new InvalidOperationException("The Chest Hunt session is already complete.");
        }

        if (_openingLifeSaver)
        {
            if (outcome != ChestHuntOutcome.LifeSaver)
            {
                _isComplete = true;
                return new ChestHuntStep(
                    _strategyState,
                    ChestHuntAction.Stop,
                    "The Life Saver could not be verified.");
            }

            _openingLifeSaver = false;
            _strategyState = ChestHuntStrategyState.Normal;
            return new ChestHuntStep(
                _strategyState,
                ChestHuntAction.ContinueOpeningGrid,
                "The Life Saver was opened.");
        }

        _remainingChests.Dequeue();
        ChestHuntStep step = ChestHuntStrategy.Decide(
            _openedChestIndex,
            _strategyState,
            outcome,
            settings);
        _openedChestIndex++;
        _strategyState = step.NextState;

        if (step.Action == ChestHuntAction.Stop)
        {
            _isComplete = true;
        }
        else if (step.Action == ChestHuntAction.OpenLifeSaver)
        {
            _openingLifeSaver = true;
        }

        return step;
    }
}
