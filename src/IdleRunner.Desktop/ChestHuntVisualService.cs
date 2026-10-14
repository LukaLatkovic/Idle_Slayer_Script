using IdleRunner.Core;

namespace IdleRunner.Desktop;

public sealed class ChestHuntVisualService
{
    private readonly IGameFrameSource _frameSource;
    private readonly ChestHuntCalibrationStore _store;
    private ChestHuntCalibration? _calibration;

    public ChestHuntVisualService(
        IGameFrameSource frameSource,
        ChestHuntCalibrationStore store)
    {
        _frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _calibration = store.Load();
    }

    public bool HasCalibration => Volatile.Read(ref _calibration) is not null;

    public async Task<ChestHuntSaverMatch> CalibrateAsync(CancellationToken cancellationToken)
    {
        GameFrame frame = await _frameSource.CaptureAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("A Chest Hunt frame could not be captured.");

        ChestHuntCalibration calibration = ChestHuntCalibration.FromFrame(frame);
        ChestHuntSaverMatch saver = calibration.LocateSaver(frame);
        if (!saver.IsMatch)
        {
            throw new InvalidOperationException("The highlighted Chest Hunt saver could not be verified.");
        }

        _store.Save(calibration);
        Volatile.Write(ref _calibration, calibration);
        return saver;
    }
}
