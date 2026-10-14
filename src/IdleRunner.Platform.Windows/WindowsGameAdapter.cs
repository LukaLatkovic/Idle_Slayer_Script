using System.ComponentModel;
using System.Runtime.InteropServices;
using IdleRunner.Core;

namespace IdleRunner.Platform.Windows;

public sealed partial class WindowsGameAdapter : IGameAdapter
{
    private const string GameWindowTitle = "Idle Slayer";
    private const ushort VirtualKeyUp = 0x26;
    private const ushort VirtualKeyRight = 0x27;
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const int ShowWindowRestore = 9;

    private nint _gameWindow;

    public Task<GameSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        nint window = NativeMethods.FindWindow(null, GameWindowTitle);
        _gameWindow = window;

        if (window == nint.Zero)
        {
            return Task.FromResult(new GameSnapshot(
                GameConnectionState.NotFound,
                isForeground: false,
                clientWidth: 0,
                clientHeight: 0));
        }

        if (!NativeMethods.IsWindowVisible(window))
        {
            return Task.FromResult(new GameSnapshot(
                GameConnectionState.Hidden,
                isForeground: false,
                clientWidth: 0,
                clientHeight: 0));
        }

        if (NativeMethods.IsIconic(window))
        {
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

        return Task.FromResult(new GameSnapshot(
            GameConnectionState.Ready,
            NativeMethods.GetForegroundWindow() == window,
            clientRect.Right - clientRect.Left,
            clientRect.Bottom - clientRect.Top));
    }

    public Task<bool> ExecuteAsync(AutomationAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (action != AutomationAction.JumpAndBoost)
        {
            throw new NotSupportedException($"The action '{action}' is not supported by the Windows adapter.");
        }

        nint window = _gameWindow;
        if (window == nint.Zero
            || !NativeMethods.IsWindow(window)
            || NativeMethods.GetForegroundWindow() != window)
        {
            return Task.FromResult(false);
        }

        NativeMethods.Input[] inputs =
        [
            NativeMethods.Input.Keyboard(VirtualKeyUp, keyUp: false),
            NativeMethods.Input.Keyboard(VirtualKeyUp, keyUp: true),
            NativeMethods.Input.Keyboard(VirtualKeyRight, keyUp: false),
            NativeMethods.Input.Keyboard(VirtualKeyRight, keyUp: true)
        ];

        uint sent = NativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeMethods.Input>());

        if (sent != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows did not accept all keyboard input events.");
        }

        return Task.FromResult(true);
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

        bool activated = NativeMethods.SetForegroundWindow(window);
        return Task.FromResult(activated || NativeMethods.GetForegroundWindow() == window);
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
        internal struct Input
        {
            internal uint Type;
            internal InputUnion Data;

            internal static Input Keyboard(ushort virtualKey, bool keyUp)
            {
                return new Input
                {
                    Type = InputKeyboard,
                    Data = new InputUnion
                    {
                        Keyboard = new KeyboardInput
                        {
                            VirtualKey = virtualKey,
                            Flags = keyUp ? KeyEventKeyUp : 0
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
