namespace IdleRunner.Core;

public interface IGameAdapter
{
    Task<GameSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);

    Task ExecuteAsync(AutomationAction action, CancellationToken cancellationToken);
}
