namespace IdleRunner.Core;

public sealed class SilverBoxModule : IObservedAutomationModule
{
    private readonly IGameFrameSource _frameSource;
    private readonly Func<VisualObservationDefinition?> _observationProvider;

    public SilverBoxModule(
        IGameFrameSource frameSource,
        Func<VisualObservationDefinition?> observationProvider)
    {
        _frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
        _observationProvider = observationProvider
            ?? throw new ArgumentNullException(nameof(observationProvider));
    }

    public string Name => "Silver Box collector";

    public AutomationAction Action => AutomationAction.CollectSilverBox;

    public TimeSpan GetInterval(AutomationSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        return TimeSpan.FromMilliseconds(500);
    }

    public bool CanRun(GameSnapshot snapshot, AutomationSettings settings)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        return settings.CollectSilverBoxesEnabled && snapshot.CanReceiveInput(settings);
    }

    public async Task<bool> CanRunAsync(
        GameSnapshot snapshot,
        AutomationSettings settings,
        CancellationToken cancellationToken)
    {
        if (!CanRun(snapshot, settings))
        {
            return false;
        }

        VisualObservationDefinition? observation = _observationProvider();
        if (observation is null)
        {
            return false;
        }

        GameFrame? frame = await _frameSource.CaptureAsync(cancellationToken).ConfigureAwait(false);
        return frame is not null && observation.Evaluate(frame).IsMatch;
    }
}
