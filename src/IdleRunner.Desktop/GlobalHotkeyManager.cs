using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace IdleRunner.Desktop;

public sealed partial class GlobalHotkeyManager : IDisposable
{
    private const int ToggleHotkeyId = 0x4401;
    private const int CloseHotkeyId = 0x4402;
    private const int WindowsMessageHotkey = 0x0312;
    private const uint ModifierNone = 0;
    private const uint ModifierShift = 0x0004;
    private const uint ModifierNoRepeat = 0x4000;
    private const uint VirtualKeyHome = 0x24;
    private const uint VirtualKeyEscape = 0x1B;

    private readonly nint _windowHandle;
    private readonly HwndSource _source;
    private readonly bool _toggleRegistered;
    private readonly bool _closeRegistered;
    private bool _isDisposed;

    public GlobalHotkeyManager(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        _windowHandle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_windowHandle)
            ?? throw new InvalidOperationException("Could not access the Idle Runner window message source.");

        _toggleRegistered = NativeMethods.RegisterHotKey(
            _windowHandle,
            ToggleHotkeyId,
            ModifierNone | ModifierNoRepeat,
            VirtualKeyHome);

        if (!_toggleRegistered)
        {
            ToggleRegistrationError = new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Could not register the Home hotkey.");
        }

        _closeRegistered = NativeMethods.RegisterHotKey(
            _windowHandle,
            CloseHotkeyId,
            ModifierShift | ModifierNoRepeat,
            VirtualKeyEscape);

        if (!_closeRegistered)
        {
            CloseRegistrationError = new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Could not register the Shift+Escape hotkey.");
        }

        _source.AddHook(WindowMessageHook);
    }

    public bool IsToggleRegistered => _toggleRegistered;

    public bool IsCloseRegistered => _closeRegistered;

    public Win32Exception? ToggleRegistrationError { get; }

    public Win32Exception? CloseRegistrationError { get; }

    public event EventHandler? ToggleRequested;

    public event EventHandler? CloseRequested;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _source.RemoveHook(WindowMessageHook);

        if (_toggleRegistered)
        {
            NativeMethods.UnregisterHotKey(_windowHandle, ToggleHotkeyId);
        }

        if (_closeRegistered)
        {
            NativeMethods.UnregisterHotKey(_windowHandle, CloseHotkeyId);
        }
    }

    private nint WindowMessageHook(
        nint window,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        if (message != WindowsMessageHotkey)
        {
            return nint.Zero;
        }

        switch (wordParameter.ToInt32())
        {
            case ToggleHotkeyId:
                handled = true;
                ToggleRequested?.Invoke(this, EventArgs.Empty);
                break;
            case CloseHotkeyId:
                handled = true;
                CloseRequested?.Invoke(this, EventArgs.Empty);
                break;
        }

        return nint.Zero;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool RegisterHotKey(
            nint window,
            int identifier,
            uint modifiers,
            uint virtualKey);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool UnregisterHotKey(nint window, int identifier);
    }
}
