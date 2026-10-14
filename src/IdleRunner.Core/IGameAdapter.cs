namespace IdleRunner.Core;

public interface IGameAdapter
{
    Task<GameSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);

    Task<bool> TryActivateAsync(CancellationToken cancellationToken);

    Task<bool> ExecuteAsync(AutomationAction action, CancellationToken cancellationToken);
}
