using IdleRunner.Core;

namespace IdleRunner.Desktop;

public sealed class SilverBoxVisualService
{
    private readonly IGameFrameSource _frameSource;
    private readonly SilverBoxCalibrationStore _store;
    private VisualObservationDefinition? _observation;

    public SilverBoxVisualService(
        IGameFrameSource frameSource,
        SilverBoxCalibrationStore store)
    {
        _frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _observation = store.Load()?.CreateObservation();
    }

    public bool HasCalibration => Volatile.Read(ref _observation) is not null;

    public VisualObservationDefinition? Observation => Volatile.Read(ref _observation);

    public async Task CalibrateAsync(CancellationToken cancellationToken)
    {
        GameFrame frame = await _frameSource.CaptureAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("A game frame could not be captured for calibration.");

        SilverBoxCalibration calibration = SilverBoxCalibration.FromFrame(frame);
        _store.Save(calibration);
        Volatile.Write(ref _observation, calibration.CreateObservation());
    }
}
