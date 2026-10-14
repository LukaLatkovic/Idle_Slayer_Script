namespace IdleRunner.Core;

public sealed class ChestHuntCalibration
{
    public const int RequiredWidth = 1_280;
    public const int RequiredHeight = 720;
    private const int FirstCenterX = 212;
    private const int FirstCenterY = 273;
    private const int CellSpacing = 95;
    private const int SaverTemplateSize = 80;
    private const int ClosedChestTemplateSize = 70;
    private const int YellowSignatureMinimum = 100;
    private const double MinimumSaverScore = 0.88;
    private const double MinimumClosedChestScore = 0.90;
    private const int MinimumCalibrationClosedChestMatches = 5;
    private const int MinimumOutcomeColorPixels = 4;
    private static readonly IReadOnlyList<ChestHuntGridPosition> GridPositions = CreateGridPositions();
    private static readonly FrameRectangle OutcomeBannerRegion = new(300, 140, 680, 110);
    private static readonly FrameRectangle MimicSignatureRegion = new(428, 205, 13, 13);
    private static readonly FrameRectangle DoubleRewardSignatureRegion = new(494, 204, 13, 13);
    private static readonly FrameRectangle EndButtonRegion = new(530, 665, 220, 55);
    private static readonly FrameRectangle PerfectChestRegion = new(447, 429, 20, 20);

    public ChestHuntCalibration(
        LuminanceTemplate saverTemplate,
        LuminanceTemplate closedChestTemplate)
    {
        SaverTemplate = saverTemplate ?? throw new ArgumentNullException(nameof(saverTemplate));
        ClosedChestTemplate = closedChestTemplate
            ?? throw new ArgumentNullException(nameof(closedChestTemplate));

        if (saverTemplate.Width != SaverTemplateSize || saverTemplate.Height != SaverTemplateSize)
        {
            throw new ArgumentException("The Chest Hunt saver template has unexpected dimensions.", nameof(saverTemplate));
        }

        if (closedChestTemplate.Width != ClosedChestTemplateSize
            || closedChestTemplate.Height != ClosedChestTemplateSize)
        {
            throw new ArgumentException(
                "The closed Chest Hunt chest template has unexpected dimensions.",
                nameof(closedChestTemplate));
        }
    }

    public LuminanceTemplate SaverTemplate { get; }

    public LuminanceTemplate ClosedChestTemplate { get; }

    public static IReadOnlyList<ChestHuntGridPosition> Positions => GridPositions;

    public static ChestHuntCalibration FromFrame(GameFrame frame)
    {
        ValidateFrame(frame);

        ChestHuntGridPosition? saverPosition = null;

        foreach (ChestHuntGridPosition position in GridPositions)
        {
            if (CountYellowSignaturePixels(frame, position) < YellowSignatureMinimum)
            {
                continue;
            }

            if (saverPosition is not null)
            {
                throw new ArgumentException("More than one Chest Hunt saver marker was found.", nameof(frame));
            }

            saverPosition = position;
        }

        if (saverPosition is null)
        {
            throw new ArgumentException("The highlighted Chest Hunt saver was not found.", nameof(frame));
        }

        LuminanceTemplate closedChestTemplate = FindClosedChestTemplate(
            frame,
            saverPosition.Value);

        return new ChestHuntCalibration(
            LuminanceTemplate.FromFrame(frame, GetSaverTemplateRegion(saverPosition.Value)),
            closedChestTemplate);
    }

    public ChestHuntSaverMatch LocateSaver(GameFrame frame)
    {
        ValidateFrame(frame);

        ChestHuntGridPosition bestPosition = GridPositions[0];
        double bestScore = 0;

        foreach (ChestHuntGridPosition position in GridPositions)
        {
            FrameRectangle region = GetSaverTemplateRegion(position);
            double score = LuminanceTemplateMatcher.Compare(
                frame,
                region.Left,
                region.Top,
                SaverTemplate);

            if (score > bestScore)
            {
                bestPosition = position;
                bestScore = score;
            }
        }

        return new ChestHuntSaverMatch(
            bestPosition,
            bestScore,
            bestScore >= MinimumSaverScore);
    }

    public ChestHuntFrameObservation Analyze(GameFrame frame)
    {
        ValidateFrame(frame);

        ChestHuntSaverMatch saver = LocateSaver(frame);
        List<ChestHuntGridPosition> closedPositions = new(30);

        foreach (ChestHuntGridPosition position in GridPositions)
        {
            if (GetClosedChestScore(frame, position) >= MinimumClosedChestScore)
            {
                closedPositions.Add(position);
            }
        }

        return new ChestHuntFrameObservation(saver, closedPositions);
    }

    public ChestHuntOutcome DetectOutcome(
        GameFrame frame,
        ChestHuntGridPosition openedPosition)
    {
        ValidateFrame(frame);

        if (IsHuntEndVisible(frame))
        {
            return ChestHuntOutcome.HuntEnded;
        }

        if (CountPixels(frame, DoubleRewardSignatureRegion, IsBrightGreen) >= 1
            || CountPixels(frame, OutcomeBannerRegion, IsBrightGreen) >= MinimumOutcomeColorPixels)
        {
            return ChestHuntOutcome.DoubleReward;
        }

        if (CountPixels(frame, MimicSignatureRegion, IsBrightRed) >= 1
            || CountPixels(frame, OutcomeBannerRegion, IsBrightRed) >= MinimumOutcomeColorPixels)
        {
            return ChestHuntOutcome.Mimic;
        }

        return GetClosedChestScore(frame, openedPosition) < MinimumClosedChestScore
            ? ChestHuntOutcome.Reward
            : ChestHuntOutcome.Unknown;
    }

    public bool IsHuntEndVisible(GameFrame frame)
    {
        ValidateFrame(frame);
        return CountPixels(frame, EndButtonRegion, IsDarkRed) >= MinimumOutcomeColorPixels;
    }

    public bool IsPerfectChestVisible(GameFrame frame)
    {
        ValidateFrame(frame);
        return CountPixels(frame, PerfectChestRegion, IsBrightOrange) >= MinimumOutcomeColorPixels;
    }

    public bool IsSaverVisible(GameFrame frame)
    {
        return LocateSaver(frame).IsMatch;
    }

    public double GetClosedChestScore(
        GameFrame frame,
        ChestHuntGridPosition position)
    {
        ValidateFrame(frame);
        FrameRectangle region = GetClosedChestTemplateRegion(position);
        return LuminanceTemplateMatcher.Compare(
            frame,
            region.Left,
            region.Top,
            ClosedChestTemplate);
    }

    public bool IsClosedChest(GameFrame frame, ChestHuntGridPosition position)
    {
        return GetClosedChestScore(frame, position) >= MinimumClosedChestScore;
    }

    private static IReadOnlyList<ChestHuntGridPosition> CreateGridPositions()
    {
        List<ChestHuntGridPosition> positions = new(30);
        for (int row = 1; row <= 3; row++)
        {
            for (int column = 1; column <= 10; column++)
            {
                positions.Add(new ChestHuntGridPosition(
                    row,
                    column,
                    FirstCenterX + ((column - 1) * CellSpacing),
                    FirstCenterY + ((row - 1) * CellSpacing)));
            }
        }

        return positions;
    }

    private static int CountYellowSignaturePixels(
        GameFrame frame,
        ChestHuntGridPosition position)
    {
        FrameRectangle region = GetSaverTemplateRegion(position);
        int count = 0;

        for (int y = region.Top; y < region.Bottom; y++)
        {
            for (int x = region.Left; x < region.Right; x++)
            {
                RgbColor color = frame.GetPixel(x, y);
                if (color.Red >= 220 && color.Green >= 200 && color.Blue <= 60)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static LuminanceTemplate FindClosedChestTemplate(
        GameFrame frame,
        ChestHuntGridPosition saverPosition)
    {
        LuminanceTemplate? bestTemplate = null;
        int bestMatchCount = -1;
        double bestTotalScore = -1;

        foreach (ChestHuntGridPosition candidate in GridPositions)
        {
            if (candidate == saverPosition)
            {
                continue;
            }

            LuminanceTemplate template = LuminanceTemplate.FromFrame(
                frame,
                GetClosedChestTemplateRegion(candidate));
            int matchCount = 0;
            double totalScore = 0;

            foreach (ChestHuntGridPosition position in GridPositions)
            {
                if (position == saverPosition)
                {
                    continue;
                }

                FrameRectangle region = GetClosedChestTemplateRegion(position);
                double score = LuminanceTemplateMatcher.Compare(
                    frame,
                    region.Left,
                    region.Top,
                    template);
                totalScore += score;

                if (score >= MinimumClosedChestScore)
                {
                    matchCount++;
                }
            }

            if (matchCount > bestMatchCount
                || (matchCount == bestMatchCount && totalScore > bestTotalScore))
            {
                bestTemplate = template;
                bestMatchCount = matchCount;
                bestTotalScore = totalScore;
            }
        }

        if (bestTemplate is null || bestMatchCount < MinimumCalibrationClosedChestMatches)
        {
            throw new ArgumentException(
                "A stable closed-chest pattern was not found in the Chest Hunt grid.",
                nameof(frame));
        }

        return bestTemplate;
    }

    private static FrameRectangle GetSaverTemplateRegion(ChestHuntGridPosition position)
    {
        return new FrameRectangle(
            position.CenterX - (SaverTemplateSize / 2),
            position.CenterY - (SaverTemplateSize / 2),
            SaverTemplateSize,
            SaverTemplateSize);
    }

    private static FrameRectangle GetClosedChestTemplateRegion(ChestHuntGridPosition position)
    {
        return new FrameRectangle(
            position.CenterX - (ClosedChestTemplateSize / 2),
            position.CenterY - (ClosedChestTemplateSize / 2),
            ClosedChestTemplateSize,
            ClosedChestTemplateSize);
    }

    private static int CountPixels(
        GameFrame frame,
        FrameRectangle region,
        Func<RgbColor, bool> predicate)
    {
        int count = 0;
        for (int y = region.Top; y < region.Bottom; y++)
        {
            for (int x = region.Left; x < region.Right; x++)
            {
                if (predicate(frame.GetPixel(x, y)))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static bool IsBrightGreen(RgbColor color)
    {
        return color.Green >= 180 && color.Red <= 80 && color.Blue <= 80;
    }

    private static bool IsBrightRed(RgbColor color)
    {
        return color.Red >= 180 && color.Green <= 80 && color.Blue <= 80;
    }

    private static bool IsDarkRed(RgbColor color)
    {
        return color.Red is >= 150 and <= 205
            && color.Green <= 30
            && color.Blue <= 30;
    }

    private static bool IsBrightOrange(RgbColor color)
    {
        return color.Red >= 220
            && color.Green is >= 110 and <= 170
            && color.Blue is >= 25 and <= 85;
    }

    private static void ValidateFrame(GameFrame frame)
    {
        if (frame is null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        if (frame.Width != RequiredWidth || frame.Height != RequiredHeight)
        {
            throw new ArgumentException(
                $"Chest Hunt calibration requires a {RequiredWidth} x {RequiredHeight} frame.",
                nameof(frame));
        }
    }
}
