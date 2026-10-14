namespace IdleRunner.Core;

public sealed class GameSnapshot
{
    public GameSnapshot(
        GameConnectionState connectionState,
        bool isForeground,
        int clientWidth,
        int clientHeight)
    {
        ConnectionState = connectionState;
        IsForeground = isForeground;
        ClientWidth = Math.Max(0, clientWidth);
        ClientHeight = Math.Max(0, clientHeight);
    }

    public GameConnectionState ConnectionState { get; }

    public bool IsForeground { get; }

    public int ClientWidth { get; }

    public int ClientHeight { get; }

    public bool CanReceiveInput(AutomationSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        return ConnectionState == GameConnectionState.Ready
            && IsForeground
            && ClientWidth == settings.RequiredClientWidth
            && ClientHeight == settings.RequiredClientHeight;
    }

    public string Describe(AutomationSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        return ConnectionState switch
        {
            GameConnectionState.NotFound => "Idle Slayer was not found.",
            GameConnectionState.Hidden => "Idle Slayer is hidden.",
            GameConnectionState.Minimized => "Idle Slayer is minimized.",
            _ when !IsForeground => "Idle Slayer is not the foreground window.",
            _ when ClientWidth != settings.RequiredClientWidth
                || ClientHeight != settings.RequiredClientHeight =>
                $"Expected a {settings.RequiredClientWidth} x {settings.RequiredClientHeight} client area; "
                + $"detected {ClientWidth} x {ClientHeight}.",
            _ => "Idle Slayer is ready for input."
        };
    }
}
