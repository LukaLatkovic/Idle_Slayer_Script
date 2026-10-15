using IdleRunner.Core;

namespace IdleRunner.Core.Tests;

internal static class Program
{
    private static readonly AutomationSettings Settings = new();

    public static async Task<int> Main()
    {
        (string Name, Func<Task> Test)[] tests =
        [
            ("Settings are normalized", SettingsAreNormalizedAsync),
            ("Legacy settings are mapped", LegacySettingsAreMappedAsync),
            ("Game frame preserves BGRA pixels", GameFramePreservesPixelsAsync),
            ("Luminance matching tolerates brightness changes", LuminanceMatchingToleratesBrightnessAsync),
            ("Template search locates the strongest shape", TemplateSearchLocatesStrongestShapeAsync),
            ("Named observation requires multiple visual anchors", NamedObservationRequiresMultipleAnchorsAsync),
            ("Gameplay HUD calibration recognizes its source frame", GameplayHudCalibrationRecognizesSourceAsync),
            ("Silver Box calibration rejects a missing icon", SilverBoxCalibrationRejectsMissingIconAsync),
            ("Silver Box legacy marker works without calibration", SilverBoxLegacyMarkerWorksWithoutCalibrationAsync),
            ("Chest Hunt normal strategy opens the Life Saver after two chests", ChestHuntNormalStrategyAsync),
            ("Chest Hunt perfect strategy preserves its search limits", ChestHuntPerfectStrategyAsync),
            ("Chest Hunt special outcomes override the strategy", ChestHuntSpecialOutcomesAsync),
            ("Chest Hunt timing follows Lockpicking availability", ChestHuntTimingFollowsSettingsAsync),
            ("Chest Hunt calibration locates a moved Saver", ChestHuntCalibrationLocatesSaverAsync),
            ("Chest Hunt observation rejects an already-open grid", ChestHuntObservationRejectsOpenGridAsync),
            ("Chest Hunt outcomes require visual evidence", ChestHuntOutcomesRequireEvidenceAsync),
            ("Chest Hunt result detection rejects decorative colors", ChestHuntResultDetectionRejectsDecorationsAsync),
            ("Chest Hunt session routes the Life Saver", ChestHuntSessionRoutesLifeSaverAsync),
            ("Chest Hunt session resumes an open grid", ChestHuntSessionResumesOpenGridAsync),
            ("Bonus Stage visual states are recognized", BonusStageVisualStatesAreRecognizedAsync),
            ("Bonus Stage 2 legacy routes are complete", BonusStage2LegacyRoutesAreCompleteAsync),
            ("Paused engine does not send input", PausedEngineDoesNotSendInputAsync),
            ("Missing game does not send input", MissingGameDoesNotSendInputAsync),
            ("Background game does not send input", BackgroundGameDoesNotSendInputAsync),
            ("Wrong client size does not send input", WrongClientSizeDoesNotSendInputAsync),
            ("Ready game receives input", ReadyGameReceivesInputAsync),
            ("Visual gate blocks input", VisualGateBlocksInputAsync),
            ("Observed Silver Box triggers its action", ObservedSilverBoxTriggersActionAsync),
            ("Module interval prevents immediate duplicate input", ModuleIntervalPreventsDuplicateInputAsync),
            ("Rejected input is not reported as executed", RejectedInputIsNotReportedAsync)
        ];

        int failures = 0;

        foreach ((string name, Func<Task> test) in tests)
        {
            try
            {
                await test();
                Console.WriteLine($"PASS  {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL  {name}: {exception.Message}");
            }
        }

        Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
        return failures == 0 ? 0 : 1;
    }

    private static Task SettingsAreNormalizedAsync()
    {
        AutomationSettings normalized = new AutomationSettings
        {
            JumpIntervalMilliseconds = -10,
            AutoBuyIntervalMinutes = 0,
            AutoAscendIntervalMinutes = int.MaxValue,
            PortalCyclePosition = 20,
            RequiredClientWidth = 0,
            RequiredClientHeight = -1
        }.Normalize();

        AssertEqual(AutomationSettings.MinimumIntervalMilliseconds, normalized.JumpIntervalMilliseconds);
        AssertEqual(AutomationSettings.MinimumTimerMinutes, normalized.AutoBuyIntervalMinutes);
        AssertEqual(AutomationSettings.MaximumTimerMinutes, normalized.AutoAscendIntervalMinutes);
        AssertEqual(9, normalized.PortalCyclePosition);
        AssertEqual(1, normalized.RequiredClientWidth);
        AssertEqual(1, normalized.RequiredClientHeight);
        return Task.CompletedTask;
    }

    private static Task BonusStageVisualStatesAreRecognizedAsync()
    {
        byte[] pixels = new byte[1_280 * 720 * 4];
        SetBgraPixel(pixels, 1_280, 660, 254, 0xFF, 0xE7, 0x37);
        SetBgraPixel(pixels, 1_280, 638, 236, 0xFF, 0xBB, 0x31);
        SetBgraPixel(pixels, 1_280, 180, 498, 0x16, 0x17, 0x1F);
        SetBgraPixel(pixels, 1_280, 454, 91, 0xE1, 0xE0, 0xE2);
        SetBgraPixel(pixels, 1_280, 775, 448, 0xFF, 0xFF, 0xFF);
        SetBgraPixel(pixels, 1_280, 716, 600, 0xAF, 0x00, 0x00);
        SetBgraPixel(pixels, 1_280, 615, 590, 0x00, 0xA4, 0x00);
        GameFrame frame = new(1_280, 720, pixels);

        AssertTrue(BonusStageVisualDetector.IsEntranceVisible(frame), "Expected the Bonus Stage entrance.");
        AssertTrue(BonusStageVisualDetector.IsStage3(frame), "Expected Bonus Stage 3.");
        AssertTrue(BonusStageVisualDetector.HasSpiritBoost(frame), "Expected Spirit Boost.");
        AssertTrue(BonusStageVisualDetector.IsSliderVisible(frame), "Expected the opening slider.");
        AssertTrue(BonusStageVisualDetector.IsFailureVisible(frame), "Expected the failure button.");
        AssertTrue(BonusStageVisualDetector.IsStage3FailureVisible(frame), "Expected the retry state.");
        return Task.CompletedTask;
    }

    private static Task BonusStage2LegacyRoutesAreCompleteAsync()
    {
        int[] normalCounts = [13, 23, 15, 16];
        int[] spiritCounts = [11, 23, 15, 20];
        int[] collectionCounts = [17, 20, 20, 23];
        int[] spiritCollectionCounts = [19, 20, 20, 23];

        for (int section = 1; section <= 4; section++)
        {
            BonusStage2SectionPlan normal = BonusStage2Routes.GetSection(false, section);
            BonusStage2SectionPlan spirit = BonusStage2Routes.GetSection(true, section);
            AssertEqual(normalCounts[section - 1], normal.Traversal.Count);
            AssertEqual(spiritCounts[section - 1], spirit.Traversal.Count);
            AssertEqual(collectionCounts[section - 1], normal.CollectionJumpCount);
            AssertEqual(spiritCollectionCounts[section - 1], spirit.CollectionJumpCount);
        }

        AssertEqual(94, BonusStage2Routes.GetSection(false, 1).Traversal[0].HoldMilliseconds);
        AssertEqual(5000, BonusStage2Routes.GetSection(false, 1).Traversal[12].WaitMilliseconds);
        AssertEqual(531, BonusStage2Routes.GetSection(true, 2).Traversal[22].HoldMilliseconds);
        AssertEqual(300, BonusStage2Routes.GetSection(true, 4).Traversal[19].WaitMilliseconds);
        return Task.CompletedTask;
    }

    private static Task LegacySettingsAreMappedAsync()
    {
        string[] lines =
        [
            "[Settings]",
            "iAutoBuyTimer=1",
            "iAutoAscendTimer=10",
            "bAutoAscendState=False",
            "bAutoBuyUpgradeState=True",
            "bCraftSoulBonusState=False",
            "bSkipBonusStageState=False",
            "bCraftRagePillState=False",
            "bCirclePortalsState=False",
            "iJumpSliderValue=150",
            "bNoLockpickingState=True",
            "iCirclePortalsCount=1",
            "bDimensionalState=False",
            "bBiDimensionalState=False",
            "bDisableRageState=False",
            "bNoReinforcedCrystalSaverState=False",
            "bPerfectChestHuntState=True"
        ];

        AutomationSettings settings = LegacyAutomationSettingsParser.Parse(lines);

        AssertEqual(1, settings.AutoBuyIntervalMinutes);
        AssertEqual(true, settings.AutoBuyUpgradesEnabled);
        AssertEqual(true, settings.NoLockpicking);
        AssertEqual(true, settings.PerfectChestHunt);
        AssertEqual(1, settings.PortalCyclePosition);
        AssertEqual(150, settings.JumpIntervalMilliseconds);
        AssertEqual(true, settings.CollectSilverBoxesEnabled);
        return Task.CompletedTask;
    }

    private static Task GameFramePreservesPixelsAsync()
    {
        byte[] pixels = [30, 20, 10, 255];
        GameFrame frame = new(1, 1, pixels);
        pixels[2] = 200;

        AssertEqual(new RgbColor(10, 20, 30), frame.GetPixel(0, 0));
        AssertEqual((byte)18, frame.GetLuminance(0, 0));
        AssertEqual(0, frame.MeasureLuminanceRange().Span);
        return Task.CompletedTask;
    }

    private static Task LuminanceMatchingToleratesBrightnessAsync()
    {
        byte[,] shape =
        {
            { 10, 20, 30 },
            { 40, 80, 60 },
            { 70, 90, 100 }
        };
        GameFrame reference = CreateGrayscaleFrame(5, 5, 3, shape, left: 1, top: 1);
        GameFrame brighter = CreateGrayscaleFrame(7, 7, 3, shape, left: 3, top: 3, brightnessOffset: 30);
        LuminanceTemplate template = LuminanceTemplate.FromFrame(
            reference,
            new FrameRectangle(1, 1, 3, 3));

        double score = LuminanceTemplateMatcher.Compare(brighter, 3, 3, template);

        AssertTrue(score > 0.999, $"Expected a brightness-tolerant match, received {score:F4}.");
        return Task.CompletedTask;
    }

    private static Task TemplateSearchLocatesStrongestShapeAsync()
    {
        byte[,] shape =
        {
            { 12, 65, 18 },
            { 35, 110, 42 },
            { 85, 25, 95 }
        };
        GameFrame reference = CreateGrayscaleFrame(3, 3, 0, shape, left: 0, top: 0);
        GameFrame candidate = CreateGrayscaleFrame(8, 7, 4, shape, left: 4, top: 2, brightnessOffset: 20);
        LuminanceTemplate template = LuminanceTemplate.FromFrame(
            reference,
            new FrameRectangle(0, 0, 3, 3));

        TemplateMatchResult result = LuminanceTemplateMatcher.FindBest(
            candidate,
            new FrameRectangle(0, 0, candidate.Width, candidate.Height),
            template);

        AssertEqual(4, result.Left);
        AssertEqual(2, result.Top);
        AssertTrue(result.Score > 0.999, $"Expected an exact structural match, received {result.Score:F4}.");
        return Task.CompletedTask;
    }

    private static Task NamedObservationRequiresMultipleAnchorsAsync()
    {
        byte[,] firstShape =
        {
            { 10, 90 },
            { 70, 20 }
        };
        byte[,] secondShape =
        {
            { 100, 25 },
            { 35, 80 }
        };
        GameFrame reference = CreateCompositeGrayscaleFrame(
            8,
            4,
            3,
            (firstShape, 0, 0, 0),
            (secondShape, 5, 0, 0));
        LuminanceTemplate firstTemplate = LuminanceTemplate.FromFrame(
            reference,
            new FrameRectangle(0, 0, 2, 2));
        LuminanceTemplate secondTemplate = LuminanceTemplate.FromFrame(
            reference,
            new FrameRectangle(5, 0, 2, 2));
        VisualObservationDefinition observation = new(
            "Normal gameplay HUD",
            [
                new VisualAnchorDefinition(
                    "Left action slot",
                    firstTemplate,
                    new FrameRectangle(0, 0, 4, 4),
                    minimumScore: 0.99),
                new VisualAnchorDefinition(
                    "Right action slot",
                    secondTemplate,
                    new FrameRectangle(4, 0, 4, 4),
                    minimumScore: 0.99)
            ]);
        GameFrame completeFrame = CreateCompositeGrayscaleFrame(
            8,
            4,
            5,
            (firstShape, 1, 1, 20),
            (secondShape, 5, 1, 20));
        GameFrame incompleteFrame = CreateCompositeGrayscaleFrame(
            8,
            4,
            5,
            (firstShape, 1, 1, 20));

        VisualObservationResult completeResult = observation.Evaluate(completeFrame);
        VisualObservationResult incompleteResult = observation.Evaluate(incompleteFrame);

        AssertTrue(completeResult.IsMatch, "Expected both visual anchors to match.");
        AssertEqual(false, incompleteResult.IsMatch);
        AssertEqual(2, completeResult.Anchors.Count);
        return Task.CompletedTask;
    }

    private static Task GameplayHudCalibrationRecognizesSourceAsync()
    {
        byte[] pixels = CreateGrayscalePixels(1_280, 720, 20);
        PaintCheckerPattern(pixels, 1_280, left: 76, top: 580, width: 80, height: 80, 45, 185);
        PaintCheckerPattern(pixels, 1_280, left: 1_125, top: 580, width: 80, height: 80, 70, 220);
        GameFrame sourceFrame = new(1_280, 720, pixels);
        GameFrame blankFrame = new(1_280, 720, CreateGrayscalePixels(1_280, 720, 20));

        GameplayHudCalibration calibration = GameplayHudCalibration.FromFrame(sourceFrame);
        VisualObservationDefinition observation = calibration.CreateObservation();

        AssertTrue(observation.Evaluate(sourceFrame).IsMatch, "Expected the calibrated HUD to match.");
        AssertEqual(false, observation.Evaluate(blankFrame).IsMatch);
        return Task.CompletedTask;
    }

    private static Task SilverBoxCalibrationRejectsMissingIconAsync()
    {
        GameFrame iconFrame = CreateSilverBoxFrame(hasIcon: true);
        GameFrame blankFrame = CreateSilverBoxFrame(hasIcon: false);
        GameFrame colorfulBackgroundFrame = CreateSilverBoxBackgroundFrame();
        SilverBoxCalibration calibration = SilverBoxCalibration.FromFrame(iconFrame);
        VisualObservationDefinition observation = calibration.CreateObservation();

        AssertTrue(observation.Evaluate(iconFrame).IsMatch, "Expected the calibrated Silver Box icon to match.");
        AssertEqual(false, observation.Evaluate(blankFrame).IsMatch);

        bool rejectedBlankCalibration = false;
        try
        {
            SilverBoxCalibration.FromFrame(blankFrame);
        }
        catch (ArgumentException)
        {
            rejectedBlankCalibration = true;
        }

        AssertTrue(rejectedBlankCalibration, "Expected calibration without the claim icon to be rejected.");

        bool rejectedColorfulBackground = false;
        try
        {
            SilverBoxCalibration.FromFrame(colorfulBackgroundFrame);
        }
        catch (ArgumentException)
        {
            rejectedColorfulBackground = true;
        }

        AssertTrue(
            rejectedColorfulBackground,
            "Expected a colorful background without the icon's white and black detail to be rejected.");
        return Task.CompletedTask;
    }

    private static async Task SilverBoxLegacyMarkerWorksWithoutCalibrationAsync()
    {
        byte[] pixels = new byte[1_280 * 720 * 4];
        SetBgraPixel(pixels, 1_280, 650, 36, 0xCA, 0x97, 0x00);
        GameFrame frame = new(1_280, 720, pixels);
        SilverBoxModule module = new(new FakeFrameSource(frame), () => null);
        GameSnapshot snapshot = new(GameConnectionState.Ready, true, 1_280, 720);

        bool canRun = await module.CanRunAsync(snapshot, new AutomationSettings(), CancellationToken.None);

        AssertTrue(canRun, "Expected the original Silver Box marker to work without calibration.");
    }

    private static Task ChestHuntNormalStrategyAsync()
    {
        AutomationSettings settings = new()
        {
            PerfectChestHunt = false,
            NoReinforcedCrystalSaver = false
        };

        ChestHuntStep first = ChestHuntStrategy.Decide(
            openedChestIndex: 0,
            ChestHuntStrategyState.NoMimic,
            ChestHuntOutcome.Reward,
            settings);
        ChestHuntStep second = ChestHuntStrategy.Decide(
            openedChestIndex: 1,
            first.NextState,
            ChestHuntOutcome.Reward,
            settings);

        AssertEqual(ChestHuntAction.ContinueOpeningGrid, first.Action);
        AssertEqual(ChestHuntStrategyState.NoMimic, first.NextState);
        AssertEqual(ChestHuntAction.OpenLifeSaver, second.Action);
        AssertEqual(ChestHuntStrategyState.Normal, second.NextState);

        settings.NoReinforcedCrystalSaver = true;
        ChestHuntStep withoutReinforcedSaver = ChestHuntStrategy.Decide(
            openedChestIndex: 0,
            ChestHuntStrategyState.NoMimic,
            ChestHuntOutcome.Reward,
            settings);
        AssertEqual(ChestHuntAction.OpenLifeSaver, withoutReinforcedSaver.Action);
        return Task.CompletedTask;
    }

    private static Task ChestHuntPerfectStrategyAsync()
    {
        AutomationSettings settings = new()
        {
            PerfectChestHunt = true
        };

        ChestHuntStep firstMimic = ChestHuntStrategy.Decide(
            0,
            ChestHuntStrategyState.NoMimic,
            ChestHuntOutcome.Mimic,
            settings);
        ChestHuntStep secondMimic = ChestHuntStrategy.Decide(
            1,
            firstMimic.NextState,
            ChestHuntOutcome.Mimic,
            settings);

        AssertEqual(ChestHuntStrategyState.OneMimic, firstMimic.NextState);
        AssertEqual(ChestHuntStrategyState.TwoMimics, secondMimic.NextState);

        AssertEqual(
            ChestHuntAction.ContinueOpeningGrid,
            ChestHuntStrategy.Decide(12, ChestHuntStrategyState.NoMimic, ChestHuntOutcome.Reward, settings).Action);
        AssertEqual(
            ChestHuntAction.OpenLifeSaver,
            ChestHuntStrategy.Decide(13, ChestHuntStrategyState.NoMimic, ChestHuntOutcome.Reward, settings).Action);
        AssertEqual(
            ChestHuntAction.OpenLifeSaver,
            ChestHuntStrategy.Decide(15, ChestHuntStrategyState.OneMimic, ChestHuntOutcome.Reward, settings).Action);
        AssertEqual(
            ChestHuntAction.OpenLifeSaver,
            ChestHuntStrategy.Decide(21, ChestHuntStrategyState.TwoMimics, ChestHuntOutcome.Reward, settings).Action);
        return Task.CompletedTask;
    }

    private static Task ChestHuntSpecialOutcomesAsync()
    {
        ChestHuntStep doubleReward = ChestHuntStrategy.Decide(
            4,
            ChestHuntStrategyState.OneMimic,
            ChestHuntOutcome.DoubleReward,
            Settings);
        ChestHuntStep ended = ChestHuntStrategy.Decide(
            4,
            ChestHuntStrategyState.OneMimic,
            ChestHuntOutcome.HuntEnded,
            Settings);
        ChestHuntStep afterLifeSaver = ChestHuntStrategy.Decide(
            4,
            ChestHuntStrategyState.OneMimic,
            ChestHuntOutcome.LifeSaver,
            Settings);

        AssertEqual(ChestHuntAction.OpenLifeSaver, doubleReward.Action);
        AssertEqual(ChestHuntStrategyState.Normal, doubleReward.NextState);
        AssertEqual(ChestHuntAction.Stop, ended.Action);
        AssertEqual(ChestHuntAction.ContinueOpeningGrid, afterLifeSaver.Action);
        AssertEqual(ChestHuntStrategyState.Normal, afterLifeSaver.NextState);
        return Task.CompletedTask;
    }

    private static Task ChestHuntTimingFollowsSettingsAsync()
    {
        ChestHuntTimingProfile withoutLockpicking = ChestHuntTimingProfile.FromSettings(
            new AutomationSettings { NoLockpicking = true });
        ChestHuntTimingProfile withLockpicking = ChestHuntTimingProfile.FromSettings(
            new AutomationSettings { NoLockpicking = false });

        AssertEqual(TimeSpan.FromSeconds(4), withoutLockpicking.InitialDelay);
        AssertEqual(TimeSpan.FromMilliseconds(1_500), withoutLockpicking.ChestRevealDelay);
        AssertEqual(TimeSpan.FromMilliseconds(2_500), withoutLockpicking.MimicRevealDelay);
        AssertEqual(TimeSpan.FromSeconds(2), withLockpicking.InitialDelay);
        AssertEqual(TimeSpan.FromMilliseconds(550), withLockpicking.ChestRevealDelay);
        AssertEqual(TimeSpan.FromMilliseconds(1_500), withLockpicking.MimicRevealDelay);
        return Task.CompletedTask;
    }

    private static Task ChestHuntCalibrationLocatesSaverAsync()
    {
        GameFrame calibrationFrame = CreateChestHuntFrame(saverRow: 2, saverColumn: 5);
        GameFrame movedSaverFrame = CreateChestHuntFrame(saverRow: 3, saverColumn: 7);
        ChestHuntCalibration calibration = ChestHuntCalibration.FromFrame(calibrationFrame);

        ChestHuntSaverMatch initial = calibration.LocateSaver(calibrationFrame);
        ChestHuntSaverMatch moved = calibration.LocateSaver(movedSaverFrame);

        AssertEqual(true, initial.IsMatch);
        AssertEqual(new ChestHuntGridPosition(2, 5, 592, 368), initial.Position);
        AssertEqual(true, moved.IsMatch);
        AssertEqual(new ChestHuntGridPosition(3, 7, 782, 463), moved.Position);
        AssertTrue(moved.Score > 0.99, $"Expected an exact moved Saver match, received {moved.Score:F4}.");
        return Task.CompletedTask;
    }

    private static Task ChestHuntObservationRejectsOpenGridAsync()
    {
        ChestHuntCalibration calibration = ChestHuntCalibration.FromFrame(
            CreateChestHuntFrame(saverRow: 2, saverColumn: 5));
        ChestHuntFrameObservation fresh = calibration.Analyze(
            CreateChestHuntFrame(saverRow: 2, saverColumn: 5));
        ChestHuntFrameObservation resumed = calibration.Analyze(
            CreateChestHuntFrame(
                saverRow: 2,
                saverColumn: 5,
                openedPositions: [(1, 1)]));

        AssertEqual(true, fresh.IsActive);
        AssertEqual(true, fresh.IsFresh);
        AssertEqual(true, resumed.IsActive);
        AssertEqual(false, resumed.IsFresh);
        AssertEqual(1, resumed.OpenedNonSaverCount);
        return Task.CompletedTask;
    }

    private static Task ChestHuntOutcomesRequireEvidenceAsync()
    {
        ChestHuntCalibration calibration = ChestHuntCalibration.FromFrame(
            CreateChestHuntFrame(saverRow: 2, saverColumn: 5));
        ChestHuntGridPosition openedPosition = ChestHuntCalibration.Positions[0];

        AssertEqual(
            ChestHuntOutcome.Unknown,
            calibration.DetectOutcome(
                CreateChestHuntFrame(saverRow: 2, saverColumn: 5),
                openedPosition));
        AssertEqual(
            ChestHuntOutcome.Reward,
            calibration.DetectOutcome(
                CreateChestHuntFrame(
                    saverRow: 2,
                    saverColumn: 5,
                    openedPositions: [(1, 1)]),
                openedPosition));
        AssertEqual(
            ChestHuntOutcome.Mimic,
            calibration.DetectOutcome(
                CreateChestHuntFrame(
                    saverRow: 2,
                    saverColumn: 5,
                    openedPositions: [(1, 1)],
                    outcome: ChestHuntOutcome.Mimic),
                openedPosition));
        AssertEqual(
            ChestHuntOutcome.DoubleReward,
            calibration.DetectOutcome(
                CreateChestHuntFrame(
                    saverRow: 2,
                    saverColumn: 5,
                    openedPositions: [(1, 1)],
                    outcome: ChestHuntOutcome.DoubleReward),
                openedPosition));
        AssertEqual(
            ChestHuntOutcome.HuntEnded,
            calibration.DetectOutcome(
                CreateChestHuntFrame(
                    saverRow: 2,
                    saverColumn: 5,
                    openedPositions: [(1, 1)],
                    outcome: ChestHuntOutcome.HuntEnded),
                openedPosition));
        return Task.CompletedTask;
    }

    private static Task ChestHuntSessionRoutesLifeSaverAsync()
    {
        ChestHuntGridPosition saver = new(3, 3, 402, 463);
        ChestHuntSession session = new(saver);
        AutomationSettings settings = new() { PerfectChestHunt = false };

        AssertEqual(new ChestHuntGridPosition(1, 1, 212, 273), session.NextPosition!.Value);
        ChestHuntStep first = session.RecordOutcome(ChestHuntOutcome.Reward, settings);
        AssertEqual(ChestHuntAction.ContinueOpeningGrid, first.Action);
        AssertEqual(new ChestHuntGridPosition(1, 2, 307, 273), session.NextPosition!.Value);

        ChestHuntStep second = session.RecordOutcome(ChestHuntOutcome.Reward, settings);
        AssertEqual(ChestHuntAction.OpenLifeSaver, second.Action);
        AssertEqual(true, session.IsOpeningLifeSaver);
        AssertEqual(saver, session.NextPosition!.Value);

        ChestHuntStep lifeSaver = session.RecordOutcome(ChestHuntOutcome.LifeSaver, settings);
        AssertEqual(ChestHuntStrategyState.Normal, lifeSaver.NextState);
        AssertEqual(new ChestHuntGridPosition(1, 3, 402, 273), session.NextPosition!.Value);

        ChestHuntStep ended = session.RecordOutcome(ChestHuntOutcome.HuntEnded, settings);
        AssertEqual(ChestHuntAction.Stop, ended.Action);
        AssertEqual(true, session.IsComplete);
        return Task.CompletedTask;
    }

    private static Task ChestHuntResultDetectionRejectsDecorationsAsync()
    {
        ChestHuntCalibration calibration = ChestHuntCalibration.FromFrame(
            CreateChestHuntFrame(saverRow: 2, saverColumn: 5));
        GameFrame decorations = CreateChestHuntFrame(
            saverRow: 2,
            saverColumn: 5,
            paintDecorativeDecoys: true);
        GameFrame perfectChest = CreateChestHuntFrame(
            saverRow: 2,
            saverColumn: 5,
            paintPerfectChestSignature: true);

        AssertEqual(false, calibration.IsHuntEndVisible(decorations));
        AssertEqual(false, calibration.IsPerfectChestVisible(decorations));
        AssertEqual(true, calibration.IsPerfectChestVisible(perfectChest));
        return Task.CompletedTask;
    }

    private static Task ChestHuntSessionResumesOpenGridAsync()
    {
        ChestHuntGridPosition saver = new(1, 2, 307, 273);
        ChestHuntGridPosition opened = new(1, 1, 212, 273);
        ChestHuntSession session = new(saver, [opened]);

        AssertEqual(1, session.OpenedChestCount);
        AssertEqual(new ChestHuntGridPosition(1, 3, 402, 273), session.NextPosition!.Value);
        return Task.CompletedTask;
    }

    private static async Task PausedEngineDoesNotSendInputAsync()
    {
        FakeGameAdapter adapter = new(ReadySnapshot());
        AutomationEngine engine = CreateEngine(adapter);

        await engine.RunOnceAsync(Settings);

        AssertEqual(0, adapter.ExecutionCount);
    }

    private static async Task MissingGameDoesNotSendInputAsync()
    {
        FakeGameAdapter adapter = new(new GameSnapshot(
            GameConnectionState.NotFound,
            isForeground: false,
            clientWidth: 0,
            clientHeight: 0));
        AutomationEngine engine = CreateEngine(adapter);
        engine.SetPaused(false);

        await engine.RunOnceAsync(Settings);

        AssertEqual(0, adapter.ExecutionCount);
    }

    private static async Task BackgroundGameDoesNotSendInputAsync()
    {
        FakeGameAdapter adapter = new(new GameSnapshot(
            GameConnectionState.Ready,
            isForeground: false,
            clientWidth: 1_280,
            clientHeight: 720));
        AutomationEngine engine = CreateEngine(adapter);
        engine.SetPaused(false);

        await engine.RunOnceAsync(Settings);

        AssertEqual(0, adapter.ExecutionCount);
    }

    private static async Task WrongClientSizeDoesNotSendInputAsync()
    {
        FakeGameAdapter adapter = new(new GameSnapshot(
            GameConnectionState.Ready,
            isForeground: true,
            clientWidth: 1_920,
            clientHeight: 1_080));
        AutomationEngine engine = CreateEngine(adapter);
        engine.SetPaused(false);

        await engine.RunOnceAsync(Settings);

        AssertEqual(0, adapter.ExecutionCount);
    }

    private static async Task ReadyGameReceivesInputAsync()
    {
        FakeGameAdapter adapter = new(ReadySnapshot());
        AutomationEngine engine = CreateEngine(adapter);
        int executedActionCount = 0;
        engine.ActionExecuted += (_, _) => executedActionCount++;
        engine.SetPaused(false);

        await engine.RunOnceAsync(Settings);

        AssertEqual(1, adapter.ExecutionCount);
        AssertEqual(AutomationAction.JumpAndBoost, adapter.LastAction);
        AssertEqual(1, executedActionCount);
    }

    private static async Task VisualGateBlocksInputAsync()
    {
        FakeGameAdapter adapter = new(ReadySnapshot());
        FakeAutomationGate gate = new(AutomationGateResult.Blocked("Blocked by test gate."));
        AutomationEngine engine = new(adapter, new FakeLog(), () => Settings, gate: gate);
        engine.SetPaused(false);

        await engine.RunOnceAsync(Settings);

        AssertEqual(1, gate.CheckCount);
        AssertEqual(0, adapter.ExecutionCount);
    }

    private static async Task ObservedSilverBoxTriggersActionAsync()
    {
        GameFrame frame = CreateSilverBoxFrame(hasIcon: true);
        SilverBoxCalibration calibration = SilverBoxCalibration.FromFrame(frame);
        SilverBoxModule module = new(
            new FakeFrameSource(frame),
            () => calibration.CreateObservation());
        FakeGameAdapter adapter = new(ReadySnapshot());
        AutomationEngine engine = new(
            adapter,
            new FakeLog(),
            () => Settings,
            modules: [module]);
        engine.SetPaused(false);

        await engine.RunOnceAsync(Settings);

        AssertEqual(1, adapter.ExecutionCount);
        AssertEqual(AutomationAction.CollectSilverBox, adapter.LastAction);
    }

    private static async Task RejectedInputIsNotReportedAsync()
    {
        FakeGameAdapter adapter = new(ReadySnapshot(), executionSucceeds: false);
        AutomationEngine engine = CreateEngine(adapter);
        int executedActionCount = 0;
        engine.ActionExecuted += (_, _) => executedActionCount++;
        engine.SetPaused(false);

        await engine.RunOnceAsync(Settings);

        AssertEqual(1, adapter.ExecutionCount);
        AssertEqual(0, executedActionCount);
    }

    private static async Task ModuleIntervalPreventsDuplicateInputAsync()
    {
        FakeGameAdapter adapter = new(ReadySnapshot());
        AutomationEngine engine = CreateEngine(adapter);
        engine.SetPaused(false);

        await engine.RunOnceAsync(Settings);
        await engine.RunOnceAsync(Settings);

        AssertEqual(1, adapter.ExecutionCount);
    }

    private static AutomationEngine CreateEngine(FakeGameAdapter adapter)
    {
        return new AutomationEngine(adapter, new FakeLog(), () => Settings);
    }

    private static GameSnapshot ReadySnapshot()
    {
        return new GameSnapshot(
            GameConnectionState.Ready,
            isForeground: true,
            clientWidth: 1_280,
            clientHeight: 720);
    }

    private static GameFrame CreateGrayscaleFrame(
        int width,
        int height,
        byte background,
        byte[,] shape,
        int left,
        int top,
        int brightnessOffset = 0)
    {
        return CreateCompositeGrayscaleFrame(
            width,
            height,
            background,
            (shape, left, top, brightnessOffset));
    }

    private static GameFrame CreateCompositeGrayscaleFrame(
        int width,
        int height,
        byte background,
        params (byte[,] Shape, int Left, int Top, int BrightnessOffset)[] placements)
    {
        byte[] pixels = new byte[checked(width * height * 4)];

        for (int index = 0; index < width * height; index++)
        {
            int offset = index * 4;
            pixels[offset] = background;
            pixels[offset + 1] = background;
            pixels[offset + 2] = background;
            pixels[offset + 3] = 255;
        }

        foreach ((byte[,] shape, int left, int top, int brightnessOffset) in placements)
        {
            for (int y = 0; y < shape.GetLength(0); y++)
            {
                for (int x = 0; x < shape.GetLength(1); x++)
                {
                    byte value = checked((byte)(shape[y, x] + brightnessOffset));
                    int offset = checked(((top + y) * width + left + x) * 4);
                    pixels[offset] = value;
                    pixels[offset + 1] = value;
                    pixels[offset + 2] = value;
                }
            }
        }

        return new GameFrame(width, height, pixels);
    }

    private static byte[] CreateGrayscalePixels(int width, int height, byte value)
    {
        byte[] pixels = new byte[checked(width * height * 4)];

        for (int index = 0; index < width * height; index++)
        {
            int offset = index * 4;
            pixels[offset] = value;
            pixels[offset + 1] = value;
            pixels[offset + 2] = value;
            pixels[offset + 3] = 255;
        }

        return pixels;
    }

    private static GameFrame CreateSilverBoxFrame(bool hasIcon)
    {
        byte[] pixels = CreateGrayscalePixels(1_280, 720, 30);
        if (hasIcon)
        {
            byte[] levels = [25, 65, 125, 210, 245];
            for (int y = 0; y < 20; y++)
            {
                for (int x = 0; x < 20; x++)
                {
                    byte value = levels[(x + y) % levels.Length];
                    int offset = checked(((7 + y) * 1_280 + 631 + x) * 4);
                    pixels[offset] = value;
                    pixels[offset + 1] = value;
                    pixels[offset + 2] = value;
                }
            }
        }

        return new GameFrame(1_280, 720, pixels);
    }

    private static GameFrame CreateSilverBoxBackgroundFrame()
    {
        byte[] pixels = CreateGrayscalePixels(1_280, 720, 90);
        byte[] levels = [34, 48, 62, 76, 90, 104, 118, 132, 146, 160, 174, 188, 202, 213];

        for (int y = 0; y < 20; y++)
        {
            for (int x = 0; x < 20; x++)
            {
                byte value = levels[(x + y) % levels.Length];
                int offset = checked(((7 + y) * 1_280 + 631 + x) * 4);
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
            }
        }

        return new GameFrame(1_280, 720, pixels);
    }

    private static GameFrame CreateChestHuntFrame(
        int saverRow,
        int saverColumn,
        IReadOnlyCollection<(int Row, int Column)>? openedPositions = null,
        ChestHuntOutcome outcome = ChestHuntOutcome.Unknown,
        bool paintDecorativeDecoys = false,
        bool paintPerfectChestSignature = false)
    {
        byte[] pixels = CreateGrayscalePixels(1_280, 720, 25);

        foreach (ChestHuntGridPosition position in ChestHuntCalibration.Positions)
        {
            for (int y = -40; y < 40; y++)
            {
                for (int x = -40; x < 40; x++)
                {
                    byte value = (byte)(70 + ((((x + 40) / 8) + ((y + 40) / 8)) % 3 * 45));
                    SetBgraPixel(
                        pixels,
                        1_280,
                        position.CenterX + x,
                        position.CenterY + y,
                        value,
                        value,
                        value);
                }
            }
        }

        ChestHuntGridPosition saver = ChestHuntCalibration.Positions
            .Single(position => position.Row == saverRow && position.Column == saverColumn);

        foreach ((int row, int column) in openedPositions ?? [])
        {
            ChestHuntGridPosition opened = ChestHuntCalibration.Positions
                .Single(position => position.Row == row && position.Column == column);
            PaintSolidRectangle(
                pixels,
                1_280,
                opened.CenterX - 35,
                opened.CenterY - 35,
                70,
                70,
                red: 32,
                green: 24,
                blue: 16);
        }

        PaintSaverCorner(pixels, saver.CenterX - 38, saver.CenterY - 38);
        PaintSaverCorner(pixels, saver.CenterX + 31, saver.CenterY - 38);
        PaintSaverCorner(pixels, saver.CenterX - 38, saver.CenterY + 31);
        PaintSaverCorner(pixels, saver.CenterX + 31, saver.CenterY + 31);

        if (outcome == ChestHuntOutcome.Mimic)
        {
            PaintSolidRectangle(pixels, 1_280, 430, 207, 8, 8, 255, 0, 0);
        }
        else if (outcome == ChestHuntOutcome.DoubleReward)
        {
            PaintSolidRectangle(pixels, 1_280, 496, 206, 8, 8, 0, 255, 0);
        }
        else if (outcome == ChestHuntOutcome.HuntEnded)
        {
            PaintSolidRectangle(pixels, 1_280, 620, 680, 8, 8, 175, 0, 0);
        }

        if (paintDecorativeDecoys)
        {
            PaintSolidRectangle(pixels, 1_280, 620, 680, 8, 8, 120, 58, 49);
            PaintSolidRectangle(pixels, 1_280, 450, 432, 8, 8, 255, 187, 49);
        }

        if (paintPerfectChestSignature)
        {
            PaintSolidRectangle(pixels, 1_280, 450, 432, 8, 8, 246, 143, 55);
        }

        return new GameFrame(1_280, 720, pixels);
    }

    private static void PaintSolidRectangle(
        byte[] pixels,
        int frameWidth,
        int left,
        int top,
        int width,
        int height,
        byte red,
        byte green,
        byte blue)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                SetBgraPixel(pixels, frameWidth, left + x, top + y, red, green, blue);
            }
        }
    }

    private static void PaintSaverCorner(byte[] pixels, int left, int top)
    {
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                SetBgraPixel(pixels, 1_280, left + x, top + y, 255, 230, 0);
            }
        }
    }

    private static void SetBgraPixel(
        byte[] pixels,
        int frameWidth,
        int x,
        int y,
        byte red,
        byte green,
        byte blue)
    {
        int offset = checked(((y * frameWidth) + x) * 4);
        pixels[offset] = blue;
        pixels[offset + 1] = green;
        pixels[offset + 2] = red;
        pixels[offset + 3] = 255;
    }

    private static void PaintCheckerPattern(
        byte[] pixels,
        int frameWidth,
        int left,
        int top,
        int width,
        int height,
        byte dark,
        byte light)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte value = ((x / 8) + (y / 8)) % 2 == 0 ? dark : light;
                int offset = checked(((top + y) * frameWidth + left + x) * 4);
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
            }
        }
    }

    private static void AssertEqual<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', received '{actual}'.");
        }
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class FakeGameAdapter : IGameAdapter
    {
        private readonly GameSnapshot _snapshot;
        private readonly bool _executionSucceeds;

        public FakeGameAdapter(GameSnapshot snapshot, bool executionSucceeds = true)
        {
            _snapshot = snapshot;
            _executionSucceeds = executionSucceeds;
        }

        public int ExecutionCount { get; private set; }

        public AutomationAction LastAction { get; private set; }

        public Task<GameSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_snapshot);
        }

        public Task<bool> TryActivateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }

        public Task ReleaseInputAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<bool> ExecuteAsync(AutomationAction action, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecutionCount++;
            LastAction = action;
            return Task.FromResult(_executionSucceeds);
        }
    }

    private sealed class FakeLog : IAutomationLog
    {
        public void Info(string message)
        {
        }

        public void Error(string message, Exception exception)
        {
        }
    }

    private sealed class FakeAutomationGate : IAutomationGate
    {
        private readonly AutomationGateResult _result;

        public FakeAutomationGate(AutomationGateResult result)
        {
            _result = result;
        }

        public int CheckCount { get; private set; }

        public Task<AutomationGateResult> CheckAsync(
            GameSnapshot snapshot,
            AutomationSettings settings,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeFrameSource : IGameFrameSource
    {
        private readonly GameFrame? _frame;

        public FakeFrameSource(GameFrame? frame)
        {
            _frame = frame;
        }

        public Task<GameFrame?> CaptureAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_frame);
        }
    }
}
