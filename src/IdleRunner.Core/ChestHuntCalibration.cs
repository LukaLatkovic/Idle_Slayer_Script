namespace IdleRunner.Core;

public sealed class ChestHuntCalibration
{
    public const int RequiredWidth = 1_280;
    public const int RequiredHeight = 720;
    private const int FirstCenterX = 212;
    private const int FirstCenterY = 273;
    private const int CellSpacing = 95;
    private const int TemplateSize = 80;
    private const int YellowSignatureMinimum = 100;
    private const double MinimumSaverScore = 0.88;
    private static readonly IReadOnlyList<ChestHuntGridPosition> GridPositions = CreateGridPositions();

    public ChestHuntCalibration(LuminanceTemplate saverTemplate)
    {
        SaverTemplate = saverTemplate ?? throw new ArgumentNullException(nameof(saverTemplate));

        if (saverTemplate.Width != TemplateSize || saverTemplate.Height != TemplateSize)
        {
            throw new ArgumentException("The Chest Hunt saver template has unexpected dimensions.", nameof(saverTemplate));
        }
    }

    public LuminanceTemplate SaverTemplate { get; }

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

        return new ChestHuntCalibration(
            LuminanceTemplate.FromFrame(frame, GetTemplateRegion(saverPosition.Value)));
    }

    public ChestHuntSaverMatch LocateSaver(GameFrame frame)
    {
        ValidateFrame(frame);

        ChestHuntGridPosition bestPosition = GridPositions[0];
        double bestScore = 0;

        foreach (ChestHuntGridPosition position in GridPositions)
        {
            FrameRectangle region = GetTemplateRegion(position);
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
        FrameRectangle region = GetTemplateRegion(position);
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

    private static FrameRectangle GetTemplateRegion(ChestHuntGridPosition position)
    {
        return new FrameRectangle(
            position.CenterX - (TemplateSize / 2),
            position.CenterY - (TemplateSize / 2),
            TemplateSize,
            TemplateSize);
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
