using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LazyFateAutomation.Helpers.Utils;

public static class WindowFlash {
    // From https://github.com/reiichi001/Dalamud-FlashOnTellPlugin/

    /// Stop flashing. The system restores the window to its original state.
    public const uint FLASHW_STOP = 0;

    /// Flash the window caption.
    public const uint FLASHW_CAPTION = 1;

    /// Flash the taskbar button.
    public const uint FLASHW_TRAY = 2;

    /// Flash both the window caption and taskbar button.
    /// This is equivalent to setting the FLASHW_CAPTION | FLASHW_TRAY flags.
    public const uint FLASHW_ALL = 3;

    /// Flash continuously, until the FLASHW_STOP flag is set.
    public const uint FLASHW_TIMER = 4;

    /// Flash continuously until the window comes to the foreground.
    public const uint FLASHW_TIMERNOFG = 12;

    [StructLayout(LayoutKind.Sequential)]
    public struct FLASHWINFO {
        /// The size of the structure in bytes.
        public uint cbSize;

        /// A Handle to the Window to be Flashed. The window can be either opened or minimized.
        public IntPtr hwnd;

        /// The Flash Status.
        public uint dwFlags;

        /// The number of times to Flash the window.
        public uint uCount;

        /// The rate at which the Window is to be flashed, in milliseconds. If Zero, the function uses the default cursor blink rate.
        public uint dwTimeout;
    }

    public static FLASHWINFO Default => new() {
        cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
        uCount = uint.MaxValue,
        dwTimeout = 0,
        dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
        hwnd = Process.GetCurrentProcess().MainWindowHandle,
    };

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    /// Flash the specified Window (Form) until it receives focus.
    public static bool Flash(FLASHWINFO flashinfo = default) {
        if (flashinfo.cbSize == 0)
            flashinfo = Default;
        return Win2000OrLater && FlashWindowEx(ref flashinfo);
    }

    private static bool Win2000OrLater => Environment.OSVersion.Version.Major >= 5;
}
