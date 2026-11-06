using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace DesktopBadge.Core;

internal static class NativeMethods
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    public const int WM_MOUSEACTIVATE = 0x0021;
    public const int MA_NOACTIVATE = 3;

    const uint SWP_NOSIZE = 0x0001;
    const uint SWP_NOZORDER = 0x0004;
    const uint SWP_NOACTIVATE = 0x0010;
    const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public const int HWND_TOPMOST = -1;
    public const int HWND_NOTOPMOST = -2;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    [DllImport("Shcore.dll")]
    static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    public static void MakeOverlay(IntPtr hwnd)
    {
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        // Make sure WPF's layered style survives our edit.
        var check = GetWindowLong(hwnd, GWL_EXSTYLE);
        if ((check & WS_EX_NOACTIVATE) == 0)
        {
            SetWindowLong(hwnd, GWL_EXSTYLE, check | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }
    }

    /// <summary>Explicitly sets the window's topmost z-order state.</summary>
    public static void SetTopmost(IntPtr hwnd, bool topmost)
    {
        var hWndInsertAfter = topmost ? new IntPtr(HWND_TOPMOST) : new IntPtr(HWND_NOTOPMOST);
        SetWindowPos(hwnd, hWndInsertAfter, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public static void SetClickThrough(IntPtr hwnd, bool enabled)
    {
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        var updated = enabled ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
        if (updated != ex) SetWindowLong(hwnd, GWL_EXSTYLE, updated);
    }

    public static RECT GetRect(IntPtr hwnd)
    {
        GetWindowRect(hwnd, out var rect);
        return rect;
    }

    public static void MoveTo(IntPtr hwnd, int x, int y) =>
        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

    /// <summary>Effective DPI scale of a monitor, with a safe fallback.</summary>
    public static double GetScale(WinForms.Screen screen, IntPtr hwnd)
    {
        try
        {
            var pt = new POINT
            {
                X = screen.Bounds.Left + screen.Bounds.Width / 2,
                Y = screen.Bounds.Top + screen.Bounds.Height / 2
            };
            var monitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero &&
                GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 &&
                dpiX > 0)
            {
                return dpiX / 96.0;
            }
        }
        catch
        {
            // Shcore is not available on very old systems or some sandboxes.
        }

        var windowDpi = hwnd != IntPtr.Zero ? GetDpiForWindow(hwnd) : 0;
        return windowDpi > 0 ? windowDpi / 96.0 : 1.0;
    }

    public static void UseDarkTitleBar(IntPtr hwnd)
    {
        // DWMWA_USE_IMMERSIVE_DARK_MODE is 20 on current builds, 19 on older Win10.
        var enabled = 1;
        if (DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref enabled, sizeof(int));
    }
}
