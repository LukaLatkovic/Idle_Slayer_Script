namespace IdleRunner.Core;

public interface IGamePointerAdapter
{
    Task<bool> ClickClientPointAsync(
        int clientX,
        int clientY,
        CancellationToken cancellationToken);
}
