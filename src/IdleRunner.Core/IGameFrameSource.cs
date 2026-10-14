namespace IdleRunner.Core;

public interface IGameFrameSource
{
    Task<GameFrame?> CaptureAsync(CancellationToken cancellationToken);
}
