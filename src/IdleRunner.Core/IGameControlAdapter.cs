namespace IdleRunner.Core;

public interface IGameControlAdapter
{
    Task<bool> HoldJumpAsync(
        int durationMilliseconds,
        CancellationToken cancellationToken);

    Task<bool> DragClientPointAsync(
        int startX,
        int startY,
        int endX,
        int endY,
        CancellationToken cancellationToken);
}
