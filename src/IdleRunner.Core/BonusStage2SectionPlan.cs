namespace IdleRunner.Core;

public sealed class BonusStage2SectionPlan
{
    public BonusStage2SectionPlan(
        IReadOnlyList<BonusStageJumpCommand> traversal,
        BonusStageJumpCommand? collectionLeadIn,
        int collectionJumpCount)
    {
        Traversal = traversal ?? throw new ArgumentNullException(nameof(traversal));
        CollectionLeadIn = collectionLeadIn;
        CollectionJumpCount = collectionJumpCount;
    }

    public IReadOnlyList<BonusStageJumpCommand> Traversal { get; }

    public BonusStageJumpCommand? CollectionLeadIn { get; }

    public int CollectionJumpCount { get; }
}
