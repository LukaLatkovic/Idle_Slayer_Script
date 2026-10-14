using System.ComponentModel;
using System.Runtime.InteropServices;
using IdleRunner.Core;

namespace IdleRunner.Platform.Windows;

public sealed partial class WindowsGameAdapter : IGameAdapter
{
    private const string GameWindowTitle = "Idle Slayer";
    private const int InputPulseMilliseconds = 35;
    private const uint InputMouse = 0;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const int ShowWindowRestore = 9;

    private nint _gameWindow;
    private readonly object _cursorLock = new();
    private NativeMethods.Point _originalCursorPosition;
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

        return action switch
        {
            AutomationAction.JumpAndBoost => await SendJumpAndBoostAsync(window, cancellationToken)
                .ConfigureAwait(false),
            AutomationAction.CollectSilverBox => await ClickSilverBoxAsync(window, cancellationToken)
                .ConfigureAwait(false),
            _ => throw new NotSupportedException($"The action '{action}' is not supported by the Windows adapter.")
        };
    }

    private async Task<bool> SendJumpAndBoostAsync(nint window, CancellationToken cancellationToken)
    {
        if (!TryParkCursor(window))
        {
            return false;
        }

        NativeMethods.Input[] buttonDownInputs =
        [
            NativeMethods.Input.MouseButton(MouseEventLeftDown),
            NativeMethods.Input.MouseButton(MouseEventRightDown)
        ];
        NativeMethods.Input[] buttonUpInputs =
        [
            NativeMethods.Input.MouseButton(MouseEventRightUp),
            NativeMethods.Input.MouseButton(MouseEventLeftUp)
        ];

        SendInputEvents(buttonDownInputs);

        try
        {
            await Task.Delay(InputPulseMilliseconds, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            SendInputEvents(buttonUpInputs);
        }

        return true;
    }

    private async Task<bool> ClickSilverBoxAsync(nint window, CancellationToken cancellationToken)
    {
        if (!TryMoveCursorToClientPoint(
            window,
            SilverBoxCalibration.ClickX,
            SilverBoxCalibration.ClickY))
        {
            return false;
        }

        SendInputEvents([NativeMethods.Input.MouseButton(MouseEventLeftDown)]);

        try
        {
            await Task.Delay(InputPulseMilliseconds, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            SendInputEvents([NativeMethods.Input.MouseButton(MouseEventLeftUp)]);

            if (NativeMethods.GetForegroundWindow() == window)
            {
                TryParkCursor(window);
            }
            else
            {
                RestoreCursor();
            }
        }

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

        if (activated)
        {
            activated = TryParkCursor(window);
        }

        return Task.FromResult(activated);
    }

    public Task ReleaseInputAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RestoreCursor();
        return Task.CompletedTask;
    }

    private static void SendInputEvents(NativeMethods.Input[] inputs)
    {
        uint sent = NativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeMethods.Input>());

        if (sent != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows did not accept all mouse input events.");
        }
    }

    private bool TryParkCursor(nint window)
    {
        if (!NativeMethods.GetClientRect(window, out NativeMethods.Rect clientRect))
        {
            RestoreCursor();
            return false;
        }

        return TryMoveCursorToClientPoint(
            window,
            (clientRect.Right - clientRect.Left) / 2,
            (clientRect.Bottom - clientRect.Top) / 2);
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

        NativeMethods.SetCursorPos(_originalCursorPosition.X, _originalCursorPosition.Y);
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

        [LibraryImport("user32.dll", SetLastError = true)]
        internal static partial uint SendInput(
            uint inputCount,
            [In] Input[] inputs,
            int inputSize);

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

        [StructLayout(LayoutKind.Sequential)]
        internal struct Input
        {
            internal uint Type;
            internal InputUnion Data;

            internal static Input MouseButton(uint flags)
            {
                return new Input
                {
                    Type = InputMouse,
                    Data = new InputUnion
                    {
                        Mouse = new MouseInput
                        {
                            Flags = flags
                        }
                    }
                };
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct InputUnion
        {
            [FieldOffset(0)]
            internal MouseInput Mouse;

            [FieldOffset(0)]
            internal KeyboardInput Keyboard;

            [FieldOffset(0)]
            internal HardwareInput Hardware;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KeyboardInput
        {
            internal ushort VirtualKey;
            internal ushort ScanCode;
            internal uint Flags;
            internal uint Time;
            internal nuint ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MouseInput
        {
            internal int X;
            internal int Y;
            internal uint MouseData;
            internal uint Flags;
            internal uint Time;
            internal nuint ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HardwareInput
        {
            internal uint Message;
            internal ushort ParameterLow;
            internal ushort ParameterHigh;
        }
    }
}
