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
            ("Paused engine does not send input", PausedEngineDoesNotSendInputAsync),
            ("Missing game does not send input", MissingGameDoesNotSendInputAsync),
            ("Background game does not send input", BackgroundGameDoesNotSendInputAsync),
            ("Wrong client size does not send input", WrongClientSizeDoesNotSendInputAsync),
            ("Ready game receives input", ReadyGameReceivesInputAsync),
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
            RequiredClientWidth = 0,
            RequiredClientHeight = -1
        }.Normalize();

        AssertEqual(AutomationSettings.MinimumIntervalMilliseconds, normalized.JumpIntervalMilliseconds);
        AssertEqual(1, normalized.RequiredClientWidth);
        AssertEqual(1, normalized.RequiredClientHeight);
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

    private static void AssertEqual<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', received '{actual}'.");
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
