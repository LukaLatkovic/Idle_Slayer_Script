using System.ComponentModel;
using System.Runtime.InteropServices;
using IdleRunner.Core;

namespace IdleRunner.Platform.Windows;

public sealed partial class WindowsGameFrameSource : IGameFrameSource
{
    private const string GameWindowTitle = "Idle Slayer";
    private const uint DibRgbColors = 0;
    private const uint PrintWindowClientOnly = 0x00000001;
    private const uint PrintWindowRenderFullContent = 0x00000002;

    public Task<GameFrame?> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        nint window = NativeMethods.FindWindow(null, GameWindowTitle);
        if (window == nint.Zero
            || !NativeMethods.IsWindowVisible(window)
            || NativeMethods.IsIconic(window))
        {
            return Task.FromResult<GameFrame?>(null);
        }

        if (!NativeMethods.GetClientRect(window, out NativeMethods.Rect clientRect))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the Idle Slayer client area.");
        }

        int width = clientRect.Right - clientRect.Left;
        int height = clientRect.Bottom - clientRect.Top;
        if (width <= 0 || height <= 0)
        {
            return Task.FromResult<GameFrame?>(null);
        }

        GameFrame frame = CaptureClientArea(window, width, height);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<GameFrame?>(frame);
    }

    private static GameFrame CaptureClientArea(nint window, int width, int height)
    {
        nint windowDeviceContext = NativeMethods.GetDC(window);
        if (windowDeviceContext == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not access the Idle Slayer device context.");
        }

        nint memoryDeviceContext = nint.Zero;
        nint bitmap = nint.Zero;
        nint previousBitmap = nint.Zero;

        try
        {
            memoryDeviceContext = NativeMethods.CreateCompatibleDC(windowDeviceContext);
            if (memoryDeviceContext == nint.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create a frame capture device context.");
            }

            NativeMethods.BitmapInfo bitmapInfo = NativeMethods.BitmapInfo.Create(width, height);
            bitmap = NativeMethods.CreateDIBSection(
                windowDeviceContext,
                ref bitmapInfo,
                DibRgbColors,
                out nint pixelBuffer,
                nint.Zero,
                0);

            if (bitmap == nint.Zero || pixelBuffer == nint.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not allocate the frame capture bitmap.");
            }

            previousBitmap = NativeMethods.SelectObject(memoryDeviceContext, bitmap);
            if (previousBitmap == nint.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not select the frame capture bitmap.");
            }

            bool wasRendered = NativeMethods.PrintWindow(
                window,
                memoryDeviceContext,
                PrintWindowClientOnly | PrintWindowRenderFullContent);

            if (!wasRendered)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Idle Slayer did not provide a client frame.");
            }

            byte[] pixels = new byte[checked(width * height * 4)];
            Marshal.Copy(pixelBuffer, pixels, 0, pixels.Length);
            return new GameFrame(width, height, pixels);
        }
        finally
        {
            if (previousBitmap != nint.Zero && memoryDeviceContext != nint.Zero)
            {
                NativeMethods.SelectObject(memoryDeviceContext, previousBitmap);
            }

            if (bitmap != nint.Zero)
            {
                NativeMethods.DeleteObject(bitmap);
            }

            if (memoryDeviceContext != nint.Zero)
            {
                NativeMethods.DeleteDC(memoryDeviceContext);
            }

            NativeMethods.ReleaseDC(window, windowDeviceContext);
        }
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial nint FindWindow(string? className, string windowName);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsWindowVisible(nint window);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsIconic(nint window);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetClientRect(nint window, out Rect rectangle);

        [LibraryImport("user32.dll", SetLastError = true)]
        internal static partial nint GetDC(nint window);

        [LibraryImport("user32.dll")]
        internal static partial int ReleaseDC(nint window, nint deviceContext);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool PrintWindow(nint window, nint deviceContext, uint flags);

        [LibraryImport("gdi32.dll", SetLastError = true)]
        internal static partial nint CreateCompatibleDC(nint deviceContext);

        [LibraryImport("gdi32.dll", SetLastError = true)]
        internal static partial nint CreateDIBSection(
            nint deviceContext,
            ref BitmapInfo bitmapInfo,
            uint usage,
            out nint pixelBuffer,
            nint section,
            uint offset);

        [LibraryImport("gdi32.dll", SetLastError = true)]
        internal static partial nint SelectObject(nint deviceContext, nint graphicsObject);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool DeleteObject(nint graphicsObject);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool DeleteDC(nint deviceContext);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BitmapInfo
        {
            internal BitmapInfoHeader Header;
            internal uint Colors;

            internal static BitmapInfo Create(int width, int height)
            {
                return new BitmapInfo
                {
                    Header = new BitmapInfoHeader
                    {
                        Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                        Width = width,
                        Height = -height,
                        Planes = 1,
                        BitCount = 32,
                        Compression = 0
                    }
                };
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BitmapInfoHeader
        {
            internal uint Size;
            internal int Width;
            internal int Height;
            internal ushort Planes;
            internal ushort BitCount;
            internal uint Compression;
            internal uint SizeImage;
            internal int XPelsPerMeter;
            internal int YPelsPerMeter;
            internal uint ColorsUsed;
            internal uint ColorsImportant;
        }
    }
}
