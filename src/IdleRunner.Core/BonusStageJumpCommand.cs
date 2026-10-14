namespace IdleRunner.Core;

public readonly struct BonusStageJumpCommand
{
    public BonusStageJumpCommand(int holdMilliseconds, int waitMilliseconds)
    {
        HoldMilliseconds = holdMilliseconds;
        WaitMilliseconds = waitMilliseconds;
    }

    public int HoldMilliseconds { get; }

    public int WaitMilliseconds { get; }
}
