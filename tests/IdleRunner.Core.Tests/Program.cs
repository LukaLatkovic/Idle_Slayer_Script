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
            ("Paused engine does not send input", PausedEngineDoesNotSendInputAsync),
            ("Missing game does not send input", MissingGameDoesNotSendInputAsync),
            ("Background game does not send input", BackgroundGameDoesNotSendInputAsync),
            ("Wrong client size does not send input", WrongClientSizeDoesNotSendInputAsync),
            ("Ready game receives input", ReadyGameReceivesInputAsync),
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
}
