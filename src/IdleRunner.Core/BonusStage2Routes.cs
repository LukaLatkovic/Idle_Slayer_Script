namespace IdleRunner.Core;

public static class BonusStage2Routes
{
    private static readonly BonusStage2SectionPlan NormalSection1 = Plan(
        17,
        new BonusStageJumpCommand(40, 5000),
        (94, 1640), (32, 1218), (94, 600), (109, 1828), (63, 640),
        (47, 688), (78, 1906), (141, 1625), (47, 3187), (47, 734),
        (47, 750), (78, 1203), (110, 5000));

    private static readonly BonusStage2SectionPlan SpiritSection1 = Plan(
        19,
        new BonusStageJumpCommand(40, 2500),
        (94, 1640), (47, 2072), (187, 688), (31, 672), (31, 1700),
        (94, 1640), (47, 2072), (187, 688), (31, 672), (31, 1700),
        (94, 5000));

    private static readonly BonusStage2SectionPlan SharedSection2 = Plan(
        20,
        new BonusStageJumpCommand(350, 1000),
        (156, 719), (47, 687), (360, 1390), (485, 344), (406, 749),
        (78, 600), (94, 900), (109, 954), (31, 672), (515, 1344),
        (484, 297), (406, 749), (78, 600), (94, 900), (109, 954),
        (31, 672), (515, 1344), (469, 219), (297, 750), (156, 500),
        (110, 3000), (360, 2984), (531, 2313));

    private static readonly BonusStage2SectionPlan NormalSection3 = Plan(
        20,
        new BonusStageJumpCommand(900, 200),
        (109, 1203), (31, 641), (47, 1578), (47, 2437),
        (109, 1203), (31, 641), (47, 1578), (47, 2437),
        (109, 1203), (31, 641), (47, 1578), (47, 2437),
        (109, 1203), (31, 641), (47, 5125));

    private static readonly BonusStage2SectionPlan SpiritSection3 = Plan(
        20,
        new BonusStageJumpCommand(900, 200),
        (109, 1203), (31, 641), (47, 1200), (1, 3100),
        (109, 1203), (31, 641), (47, 1200), (1, 3100),
        (109, 1203), (31, 641), (47, 1200), (1, 3100),
        (109, 1203), (31, 641), (47, 5125));

    private static readonly BonusStage2SectionPlan NormalSection4 = Plan(
        23,
        null,
        (32, 1375), (641, 690), (41, 1375), (41, 1374), (641, 690),
        (41, 1373), (41, 2500), (31, 809), (41, 1375), (41, 1374),
        (641, 690), (41, 1373), (41, 1372), (641, 690), (41, 1371),
        (41, 0));

    private static readonly BonusStage2SectionPlan SpiritSection4 = Plan(
        23,
        null,
        (32, 2800), (31, 809), (41, 1200), (100, 900), (641, 500),
        (31, 850), (41, 770), (641, 400), (31, 850), (41, 870),
        (641, 300), (31, 850), (41, 790), (641, 400), (31, 850),
        (41, 840), (641, 300), (31, 850), (41, 840), (641, 300));

    public static BonusStage2SectionPlan GetSection(bool spiritBoost, int sectionNumber)
    {
        return sectionNumber switch
        {
            1 => spiritBoost ? SpiritSection1 : NormalSection1,
            2 => SharedSection2,
            3 => spiritBoost ? SpiritSection3 : NormalSection3,
            4 => spiritBoost ? SpiritSection4 : NormalSection4,
            _ => throw new ArgumentOutOfRangeException(nameof(sectionNumber))
        };
    }

    private static BonusStage2SectionPlan Plan(
        int collectionJumpCount,
        BonusStageJumpCommand? collectionLeadIn,
        params (int Hold, int Wait)[] commands)
    {
        return new BonusStage2SectionPlan(
            commands.Select(command => new BonusStageJumpCommand(command.Hold, command.Wait)).ToArray(),
            collectionLeadIn,
            collectionJumpCount);
    }
}
