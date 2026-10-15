using System.ComponentModel;
using System.Runtime.InteropServices;
using IdleRunner.Core;

namespace IdleRunner.Platform.Windows;

public sealed partial class WindowsGameAdapter : IGameAdapter, IGamePointerAdapter, IGameControlAdapter
{
    private const string GameWindowTitle = "Idle Slayer";
    private const int InputPulseMilliseconds = 35;
    private const int TargetedClickMilliseconds = 100;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventWheel = 0x0800;
    private const uint KeyEventKeyUp = 0x0002;
    private const byte VirtualKeyUp = 0x26;
    private const byte VirtualKeyRight = 0x27;
    private const byte VirtualKeyR = 0x52;
    private const int WheelDelta = 120;
    private const int ShowWindowRestore = 9;

    private nint _gameWindow;
    private readonly object _cursorLock = new();
    private readonly SemaphoreSlim _inputLock = new(1, 1);
    private NativeMethods.Point _originalCursorPosition;
    private NativeMethods.Point _programmedCursorPosition;
    private bool _cursorIsParked;

    public Task<GameSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        nint window = NativeMethods.FindWindow(null, GameWindowTitle);
        _gameWindow = window;

        if (window == nint.Zero)
        {
            RestoreCursor();
            return Task.FromResult(new GameSnapshot(
                GameConnectionState.NotFound,
                isForeground: false,
                clientWidth: 0,
                clientHeight: 0));
        }

        if (!NativeMethods.IsWindowVisible(window))
        {
            RestoreCursor();
            return Task.FromResult(new GameSnapshot(
                GameConnectionState.Hidden,
                isForeground: false,
                clientWidth: 0,
                clientHeight: 0));
        }

        if (NativeMethods.IsIconic(window))
        {
            RestoreCursor();
            return Task.FromResult(new GameSnapshot(
                GameConnectionState.Minimized,
                isForeground: false,
                clientWidth: 0,
                clientHeight: 0));
        }

        if (!NativeMethods.GetClientRect(window, out NativeMethods.Rect clientRect))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the Idle Slayer client area.");
        }

        bool isForeground = NativeMethods.GetForegroundWindow() == window;
        if (!isForeground)
        {
            RestoreCursor();
        }

        return Task.FromResult(new GameSnapshot(
            GameConnectionState.Ready,
            isForeground,
            clientRect.Right - clientRect.Left,
            clientRect.Bottom - clientRect.Top));
    }

    public async Task<bool> ExecuteAsync(AutomationAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        nint window = _gameWindow;
        if (window == nint.Zero
            || !NativeMethods.IsWindow(window)
            || NativeMethods.GetForegroundWindow() != window)
        {
            RestoreCursor();
            return false;
        }

        await _inputLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return action switch
            {
                AutomationAction.JumpAndBoost => await SendJumpAndBoostAsync(window, cancellationToken)
                    .ConfigureAwait(false),
                AutomationAction.CollectSilverBox => await ClickSilverBoxAsync(window, cancellationToken)
                    .ConfigureAwait(false),
                _ => throw new NotSupportedException($"The action '{action}' is not supported by the Windows adapter.")
            };
        }
        finally
        {
            RestoreCursor();
            _inputLock.Release();
        }
    }

    private async Task<bool> SendJumpAndBoostAsync(nint window, CancellationToken cancellationToken)
    {
        return await SendVirtualKeysAsync(
            window,
            InputPulseMilliseconds,
            cancellationToken,
            VirtualKeyUp,
            VirtualKeyRight).ConfigureAwait(false);
    }

    private async Task<bool> ClickSilverBoxAsync(nint window, CancellationToken cancellationToken)
    {
        return await ClickClientPointAsync(
            window,
            SilverBoxCalibration.ClickX,
            SilverBoxCalibration.ClickY,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ClickClientPointAsync(
        int clientX,
        int clientY,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        nint window = _gameWindow;
        if (window == nint.Zero
            || !NativeMethods.IsWindow(window)
            || NativeMethods.GetForegroundWindow() != window)
        {
            RestoreCursor();
            return false;
        }

        await _inputLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ClickClientPointAsync(window, clientX, clientY, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            RestoreCursor();
            _inputLock.Release();
        }
    }

    public async Task<bool> HoldJumpAsync(
        int durationMilliseconds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _inputLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SendVirtualKeysAsync(
                _gameWindow,
                Math.Max(InputPulseMilliseconds, durationMilliseconds),
                cancellationToken,
                VirtualKeyUp).ConfigureAwait(false);
        }
        finally
        {
            RestoreCursor();
            _inputLock.Release();
        }
    }

    public async Task<bool> DragClientPointAsync(
        int startX,
        int startY,
        int endX,
        int endY,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _inputLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            nint window = _gameWindow;
            if (!CanSendInput(window) || !TryMoveCursorToClientPoint(window, startX, startY))
            {
                return false;
            }

            NativeMethods.MouseEvent(MouseEventLeftDown, 0, 0, 0, nuint.Zero);
            try
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                if (!TryMoveCursorToClientPoint(window, endX, endY))
                {
                    return false;
                }

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                NativeMethods.MouseEvent(MouseEventLeftUp, 0, 0, 0, nuint.Zero);
            }

            await Task.Delay(75, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            RestoreCursor();
            _inputLock.Release();
        }
    }

    public async Task<bool> ScrollClientPointAsync(
        int clientX,
        int clientY,
        int wheelNotches,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _inputLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            nint window = _gameWindow;
            if (!CanSendInput(window) || !TryMoveCursorToClientPoint(window, clientX, clientY))
            {
                return false;
            }

            int direction = Math.Sign(wheelNotches);
            for (int i = 0; i < Math.Abs(wheelNotches); i++)
            {
                NativeMethods.MouseEvent(
                    MouseEventWheel,
                    0,
                    0,
                    unchecked((uint)(direction * WheelDelta)),
                    nuint.Zero);
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        finally
        {
            RestoreCursor();
            _inputLock.Release();
        }
    }

    public async Task<bool> PressRageAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _inputLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SendVirtualKeysAsync(
                _gameWindow,
                InputPulseMilliseconds,
                cancellationToken,
                VirtualKeyR).ConfigureAwait(false);
        }
        finally
        {
            _inputLock.Release();
        }
    }

    private static bool CanSendInput(nint window) =>
        window != nint.Zero
        && NativeMethods.IsWindow(window)
        && NativeMethods.GetForegroundWindow() == window;

    private async Task<bool> ClickClientPointAsync(
        nint window,
        int clientX,
        int clientY,
        CancellationToken cancellationToken)
    {
        if (!TryMoveCursorToClientPoint(window, clientX, clientY))
        {
            return false;
        }

        NativeMethods.MouseEvent(MouseEventLeftDown, 0, 0, 0, nuint.Zero);

        try
        {
            await Task.Delay(TargetedClickMilliseconds, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            NativeMethods.MouseEvent(MouseEventLeftUp, 0, 0, 0, nuint.Zero);
        }

        await Task.Delay(100, cancellationToken).ConfigureAwait(false);

        return true;
    }

    public Task<bool> TryActivateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        nint window = NativeMethods.FindWindow(null, GameWindowTitle);
        _gameWindow = window;

        if (window == nint.Zero || !NativeMethods.IsWindowVisible(window))
        {
            return Task.FromResult(false);
        }

        if (NativeMethods.IsIconic(window))
        {
            NativeMethods.ShowWindow(window, ShowWindowRestore);
        }

        bool activated = NativeMethods.SetForegroundWindow(window)
            || NativeMethods.GetForegroundWindow() == window;

        return Task.FromResult(activated);
    }

    public Task ReleaseInputAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RestoreCursor();
        return Task.CompletedTask;
    }

    private static async Task<bool> SendVirtualKeysAsync(
        nint window,
        int durationMilliseconds,
        CancellationToken cancellationToken,
        params byte[] virtualKeys)
    {
        if (!CanSendInput(window) || virtualKeys.Length == 0)
        {
            return false;
        }

        foreach (byte virtualKey in virtualKeys)
        {
            NativeMethods.KeybdEvent(virtualKey, 0, 0, nuint.Zero);
        }

        try
        {
            await Task.Delay(
                Math.Max(InputPulseMilliseconds, durationMilliseconds),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            for (int index = virtualKeys.Length - 1; index >= 0; index--)
            {
                NativeMethods.KeybdEvent(virtualKeys[index], 0, KeyEventKeyUp, nuint.Zero);
            }
        }

        await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private bool TryMoveCursorToClientPoint(nint window, int clientX, int clientY)
    {
        lock (_cursorLock)
        {
            if (!_cursorIsParked)
            {
                if (!NativeMethods.GetCursorPos(out _originalCursorPosition))
                {
                    return false;
                }

                _cursorIsParked = true;
            }

            if (!NativeMethods.GetClientRect(window, out NativeMethods.Rect clientRect)
                || clientX < clientRect.Left
                || clientY < clientRect.Top
                || clientX >= clientRect.Right
                || clientY >= clientRect.Bottom)
            {
                RestoreCursorWithinLock();
                return false;
            }

            NativeMethods.Point targetPosition = new(clientX, clientY);

            if (!NativeMethods.ClientToScreen(window, ref targetPosition)
                || !NativeMethods.SetCursorPos(targetPosition.X, targetPosition.Y))
            {
                RestoreCursorWithinLock();
                return false;
            }

            _programmedCursorPosition = targetPosition;

            return true;
        }
    }

    private void RestoreCursor()
    {
        lock (_cursorLock)
        {
            RestoreCursorWithinLock();
        }
    }

    private void RestoreCursorWithinLock()
    {
        if (!_cursorIsParked)
        {
            return;
        }

        if (!NativeMethods.GetCursorPos(out NativeMethods.Point currentPosition)
            || (currentPosition.X == _programmedCursorPosition.X
                && currentPosition.Y == _programmedCursorPosition.Y))
        {
            NativeMethods.SetCursorPos(_originalCursorPosition.X, _originalCursorPosition.Y);
        }

        _cursorIsParked = false;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll", EntryPoint = "FindWindowW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        internal static partial nint FindWindow(string? className, string windowName);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsWindow(nint window);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsWindowVisible(nint window);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsIconic(nint window);

        [LibraryImport("user32.dll")]
        internal static partial nint GetForegroundWindow();

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetForegroundWindow(nint window);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ShowWindow(nint window, int command);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetClientRect(nint window, out Rect rectangle);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ClientToScreen(nint window, ref Point point);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetCursorPos(out Point point);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetCursorPos(int x, int y);

        [LibraryImport("user32.dll", EntryPoint = "mouse_event")]
        internal static partial void MouseEvent(
            uint flags,
            uint deltaX,
            uint deltaY,
            uint data,
            nuint extraInformation);

        [LibraryImport("user32.dll", EntryPoint = "keybd_event")]
        internal static partial void KeybdEvent(
            byte virtualKey,
            byte scanCode,
            uint flags,
            nuint extraInformation);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Point
        {
            internal Point(int x, int y)
            {
                X = x;
                Y = y;
            }

            internal int X;
            internal int Y;
        }

    }
}
