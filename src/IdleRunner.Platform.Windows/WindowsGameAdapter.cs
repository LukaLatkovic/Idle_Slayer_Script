using System.ComponentModel;
using System.Runtime.InteropServices;
using IdleRunner.Core;

namespace IdleRunner.Platform.Windows;

public sealed partial class WindowsGameAdapter : IGameAdapter, IGamePointerAdapter, IGameControlAdapter
{
    private const string GameWindowTitle = "Idle Slayer";
    private const int InputPulseMilliseconds = 35;
    private const int TargetedClickMilliseconds = 100;
    private const uint InputKeyboard = 1;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventWheel = 0x0800;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventScanCode = 0x0008;
    private static readonly ScanCodeKey UpKey = new(0x48, IsExtended: true);
    private static readonly ScanCodeKey RightKey = new(0x4D, IsExtended: true);
    private static readonly ScanCodeKey RageKey = new(0x13, IsExtended: false);
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
        return await SendScanCodesAsync(
            window,
            InputPulseMilliseconds,
            cancellationToken,
            UpKey,
            RightKey).ConfigureAwait(false);
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
            return await SendScanCodesAsync(
                _gameWindow,
                Math.Max(InputPulseMilliseconds, durationMilliseconds),
                cancellationToken,
                UpKey).ConfigureAwait(false);
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
            return await SendScanCodesAsync(
                _gameWindow,
                InputPulseMilliseconds,
                cancellationToken,
                RageKey).ConfigureAwait(false);
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

    private static async Task<bool> SendScanCodesAsync(
        nint window,
        int durationMilliseconds,
        CancellationToken cancellationToken,
        params ScanCodeKey[] keys)
    {
        if (!CanSendInput(window) || keys.Length == 0)
        {
            return false;
        }

        NativeMethods.Input[] keyDownInputs = keys
            .Select(key => NativeMethods.Input.KeyboardScanCode(key, keyUp: false))
            .ToArray();
        SendKeyboardEvents(keyDownInputs);

        try
        {
            await Task.Delay(
                Math.Max(InputPulseMilliseconds, durationMilliseconds),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            NativeMethods.Input[] keyUpInputs = keys
                .Reverse()
                .Select(key => NativeMethods.Input.KeyboardScanCode(key, keyUp: true))
                .ToArray();
            SendKeyboardEvents(keyUpInputs);
        }

        await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static void SendKeyboardEvents(NativeMethods.Input[] inputs)
    {
        uint sent = NativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeMethods.Input>());

        if (sent != inputs.Length)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Windows did not accept all keyboard input events.");
        }
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

    private readonly record struct ScanCodeKey(ushort ScanCode, bool IsExtended);

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

        [LibraryImport("user32.dll", EntryPoint = "mouse_event")]
        internal static partial void MouseEvent(
            uint flags,
            uint deltaX,
            uint deltaY,
            uint data,
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

        [StructLayout(LayoutKind.Sequential)]
        internal struct Input
        {
            internal uint Type;
            internal InputUnion Data;

            internal static Input KeyboardScanCode(ScanCodeKey key, bool keyUp)
            {
                uint flags = KeyEventScanCode;
                if (key.IsExtended)
                {
                    flags |= KeyEventExtendedKey;
                }

                if (keyUp)
                {
                    flags |= KeyEventKeyUp;
                }

                return new Input
                {
                    Type = InputKeyboard,
                    Data = new InputUnion
                    {
                        Keyboard = new KeyboardInput
                        {
                            ScanCode = key.ScanCode,
                            Flags = flags
                        }
                    }
                };
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct InputUnion
        {
            // INPUT is a native union. Keeping every member preserves the
            // required x64 structure size even when only keyboard data is used.
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
