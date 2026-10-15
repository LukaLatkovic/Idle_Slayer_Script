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

    Task<bool> ScrollClientPointAsync(
        int clientX,
        int clientY,
        int wheelNotches,
        CancellationToken cancellationToken);

    Task<bool> PressRageAsync(CancellationToken cancellationToken);
}
