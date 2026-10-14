namespace IdleRunner.Core;

public sealed class GameplayHudCalibration
{
    private const int RequiredWidth = 1_280;
    private const int RequiredHeight = 720;
    private static readonly FrameRectangle LeftTemplateRegion = new(76, 580, 80, 80);
    private static readonly FrameRectangle RightTemplateRegion = new(1_125, 580, 80, 80);
    private static readonly FrameRectangle LeftSearchRegion = new(64, 568, 104, 104);
    private static readonly FrameRectangle RightSearchRegion = new(1_113, 568, 104, 104);

    public GameplayHudCalibration(
        LuminanceTemplate leftActionSlot,
        LuminanceTemplate rightActionSlot)
    {
        LeftActionSlot = leftActionSlot ?? throw new ArgumentNullException(nameof(leftActionSlot));
        RightActionSlot = rightActionSlot ?? throw new ArgumentNullException(nameof(rightActionSlot));

        if (leftActionSlot.Width != LeftTemplateRegion.Width
            || leftActionSlot.Height != LeftTemplateRegion.Height
            || rightActionSlot.Width != RightTemplateRegion.Width
            || rightActionSlot.Height != RightTemplateRegion.Height)
        {
            throw new ArgumentException("Gameplay HUD templates have unexpected dimensions.");
        }
    }

    public LuminanceTemplate LeftActionSlot { get; }

    public LuminanceTemplate RightActionSlot { get; }

    public static GameplayHudCalibration FromFrame(GameFrame frame)
    {
        if (frame is null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        if (frame.Width != RequiredWidth || frame.Height != RequiredHeight)
        {
            throw new ArgumentException(
                $"Gameplay calibration requires a {RequiredWidth} x {RequiredHeight} frame.",
                nameof(frame));
        }

        return new GameplayHudCalibration(
            LuminanceTemplate.FromFrame(frame, LeftTemplateRegion),
            LuminanceTemplate.FromFrame(frame, RightTemplateRegion));
    }

    public VisualObservationDefinition CreateObservation()
    {
        return new VisualObservationDefinition(
            "Normal gameplay HUD",
            [
                new VisualAnchorDefinition(
                    "Left action slot",
                    LeftActionSlot,
                    LeftSearchRegion,
                    minimumScore: 0.82,
                    searchStep: 2),
                new VisualAnchorDefinition(
                    "Right action slot",
                    RightActionSlot,
                    RightSearchRegion,
                    minimumScore: 0.82,
                    searchStep: 2)
            ],
            requiredMatches: 1);
    }
}
