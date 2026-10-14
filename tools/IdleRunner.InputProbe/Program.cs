using System.ComponentModel;
using System.Runtime.InteropServices;

namespace IdleRunner.InputProbe;

internal static partial class Program
{
    private const string GameWindowTitle = "Idle Slayer";
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventScanCode = 0x0008;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const ushort VirtualKeySpace = 0x20;
    private const ushort ScanCodeSpace = 0x39;

    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: IdleRunner.InputProbe <virtual-space|scan-space|mouse-left|mouse-right|mouse-jump-boost|mouse-safe-jump-boost|close-test-panels>");
            return 2;
        }

        nint gameWindow = NativeMethods.FindWindow(null, GameWindowTitle);
        if (gameWindow == nint.Zero)
        {
            Console.Error.WriteLine("Idle Slayer window was not found on the current desktop.");
            return 3;
        }

        NativeMethods.SetForegroundWindow(gameWindow);
        await Task.Delay(300);

        try
        {
            switch (args[0])
            {
                case "virtual-space":
                    Send(NativeMethods.Input.VirtualKey(VirtualKeySpace, keyUp: false));
                    await Task.Delay(1_000);
                    Send(NativeMethods.Input.VirtualKey(VirtualKeySpace, keyUp: true));
                    break;
                case "scan-space":
                    Send(NativeMethods.Input.ScanCode(ScanCodeSpace, keyUp: false));
                    await Task.Delay(1_000);
                    Send(NativeMethods.Input.ScanCode(ScanCodeSpace, keyUp: true));
                    break;
                case "mouse-left":
                    Send(NativeMethods.Input.Mouse(MouseEventLeftDown));
                    await Task.Delay(1_000);
                    Send(NativeMethods.Input.Mouse(MouseEventLeftUp));
                    break;
                case "mouse-right":
                    Send(NativeMethods.Input.Mouse(MouseEventRightDown));
                    await Task.Delay(1_000);
                    Send(NativeMethods.Input.Mouse(MouseEventRightUp));
                    break;
                case "mouse-jump-boost":
                    Send(
                        NativeMethods.Input.Mouse(MouseEventLeftDown),
                        NativeMethods.Input.Mouse(MouseEventRightDown));
                    await Task.Delay(35);
                    Send(
                        NativeMethods.Input.Mouse(MouseEventRightUp),
                        NativeMethods.Input.Mouse(MouseEventLeftUp));
                    break;
                case "mouse-safe-jump-boost":
                    await SendMousePulseAtAsync(gameWindow, 640, 330);
                    break;
                case "close-test-panels":
                    await ClickAtAsync(gameWindow, 1_235, 660);
                    await Task.Delay(250);
                    await ClickAtAsync(gameWindow, 570, 660);
                    break;
                default:
                    Console.Error.WriteLine($"Unknown input mode '{args[0]}'.");
                    return 2;
            }
        }
        catch (Win32Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 4;
        }

        return 0;
    }

    private static async Task SendMousePulseAtAsync(nint window, int clientX, int clientY)
    {
        NativeMethods.GetCursorPos(out NativeMethods.Point originalPosition);
        NativeMethods.Point safePosition = new(clientX, clientY);
        NativeMethods.ClientToScreen(window, ref safePosition);
        NativeMethods.SetCursorPos(safePosition.X, safePosition.Y);

        try
        {
            Send(
                NativeMethods.Input.Mouse(MouseEventLeftDown),
                NativeMethods.Input.Mouse(MouseEventRightDown));
            await Task.Delay(35);
            Send(
                NativeMethods.Input.Mouse(MouseEventRightUp),
                NativeMethods.Input.Mouse(MouseEventLeftUp));
            await Task.Delay(500);
        }
        finally
        {
            NativeMethods.SetCursorPos(originalPosition.X, originalPosition.Y);
        }
    }

    private static async Task ClickAtAsync(nint window, int clientX, int clientY)
    {
        NativeMethods.GetCursorPos(out NativeMethods.Point originalPosition);
        NativeMethods.Point target = new(clientX, clientY);
        NativeMethods.ClientToScreen(window, ref target);
        NativeMethods.SetCursorPos(target.X, target.Y);

        try
        {
            Send(NativeMethods.Input.Mouse(MouseEventLeftDown));
            await Task.Delay(35);
            Send(NativeMethods.Input.Mouse(MouseEventLeftUp));
        }
        finally
        {
            NativeMethods.SetCursorPos(originalPosition.X, originalPosition.Y);
        }
    }

    private static void Send(params NativeMethods.Input[] inputs)
    {
        uint sent = NativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeMethods.Input>());

        if (sent != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows rejected the diagnostic input event.");
        }
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial nint FindWindow(string? className, string windowName);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetForegroundWindow(nint window);

        [LibraryImport("user32.dll", SetLastError = true)]
        internal static partial uint SendInput(uint count, [In] Input[] inputs, int size);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetCursorPos(out Point point);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ClientToScreen(nint window, ref Point point);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetCursorPos(int x, int y);

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

            internal static Input VirtualKey(ushort virtualKey, bool keyUp)
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

            internal static Input ScanCode(ushort scanCode, bool keyUp)
            {
                return new Input
                {
                    Type = InputKeyboard,
                    Data = new InputUnion
                    {
                        Keyboard = new KeyboardInput
                        {
                            ScanCode = scanCode,
                            Flags = KeyEventScanCode | (keyUp ? KeyEventKeyUp : 0)
                        }
                    }
                };
            }

            internal static Input Mouse(uint flags)
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
