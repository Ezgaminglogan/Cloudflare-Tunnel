using System.Runtime.InteropServices;

namespace Cloudflare_Tunnel.Infrastructure;

public static class ConsoleWindowHelper
{
    private const int GWL_STYLE = -16;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_SIZEBOX = 0x00040000; // WS_THICKFRAME / sizing border
    private const int WS_MAXIMIZE = 0x01000000;

    private const int MF_BYCOMMAND = 0x00000000;
    private const int SC_MAXIMIZE = 0xF030;
    private const int SC_SIZE = 0xF000;

    private const int SW_SHOWNORMAL = 1;
    private const int SW_RESTORE = 9;

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint GA_ROOT = 2;

    private const uint WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern uint ExtractIconEx(string szFileName, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork; // Working area (excluding taskbar)
        public uint dwFlags;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

    [DllImport("user32.dll")]
    private static extern int DeleteMenu(IntPtr hMenu, int nPosition, int wFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    /// <summary>
    /// Restores any maximized window state, sets optimal console size (110x34),
    /// centers the console window on the primary/current monitor work area,
    /// and disables the Maximize button and window resize frame.
    /// </summary>
    public static void CenterAndConfigureWindow()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            IntPtr hWnd = GetTargetWindowHandle();
            if (hWnd == IntPtr.Zero) return;

            // 1. Set Custom Application Icon on window title bar and taskbar
            SetWindowIcon(hWnd);

            // 2. If currently maximized or zoomed, restore it to normal windowed mode
            if (IsZoomed(hWnd) || (GetWindowLong(hWnd, GWL_STYLE) & WS_MAXIMIZE) != 0)
            {
                ShowWindow(hWnd, SW_RESTORE);
            }

            // 3. Set optimal character dimensions on Windows conhost
            SetPreferredConsoleSize(120, 36);

            // 4. Move and center window on monitor work area (accounting for taskbar)
            CenterWindow(hWnd);

            // 5. Disable Maximize button and resizing borders
            DisableMaximize(hWnd);
        }
        catch
        {
            // Silently fallback if host restricts window manipulation
        }
    }

    /// <summary>
    /// Disables the Maximize button and window resize frame on Windows Console.
    /// </summary>
    public static void DisableMaximizeButton()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            IntPtr hWnd = GetTargetWindowHandle();
            if (hWnd != IntPtr.Zero)
            {
                DisableMaximize(hWnd);
            }
        }
        catch { }
    }

    /// <summary>
    /// Centers the console/terminal window on the current monitor's working area (accounting for taskbar).
    /// </summary>
    public static void CenterWindowOnScreen()
    {
        CenterAndConfigureWindow();
    }

    private static IntPtr GetTargetWindowHandle()
    {
        IntPtr hWnd = GetConsoleWindow();
        if (hWnd != IntPtr.Zero)
        {
            IntPtr root = GetAncestor(hWnd, GA_ROOT);
            return root != IntPtr.Zero ? root : hWnd;
        }

        try
        {
            var proc = System.Diagnostics.Process.GetCurrentProcess();
            if (proc.MainWindowHandle != IntPtr.Zero)
            {
                return proc.MainWindowHandle;
            }
        }
        catch { }

        return IntPtr.Zero;
    }

    private static void SetPreferredConsoleSize(int cols, int rows)
    {
        if (!OperatingSystem.IsWindows() || Console.IsOutputRedirected) return;

        try
        {
            int maxW = Console.LargestWindowWidth > 0 ? Console.LargestWindowWidth : 120;
            int maxH = Console.LargestWindowHeight > 0 ? Console.LargestWindowHeight : 45;

            cols = Math.Clamp(cols, 80, maxW);
            rows = Math.Clamp(rows, 25, maxH);

            // Adjust buffer width and height to be at least target window dimensions
            if (Console.BufferWidth < cols)
            {
                Console.BufferWidth = cols;
            }
            if (Console.BufferHeight < rows)
            {
                Console.BufferHeight = rows;
            }

            Console.SetWindowSize(cols, rows);

            // Keep buffer width equal to window width to eliminate horizontal scrollbar
            if (Console.BufferWidth > cols)
            {
                Console.BufferWidth = cols;
            }

            // Provide adequate scrollback buffer for edge traffic logs
            if (Console.BufferHeight < 500)
            {
                Console.BufferHeight = 500;
            }
        }
        catch
        {
            // Terminal host (e.g. Windows Terminal or VS Code) might manage dimensions externally
        }
    }

    private static void CenterWindow(IntPtr hWnd)
    {
        IntPtr hMonitor = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new MONITORINFO();
        monitorInfo.cbSize = Marshal.SizeOf<MONITORINFO>();

        int workAreaX = 0;
        int workAreaY = 0;
        int workAreaWidth = 1920;
        int workAreaHeight = 1080;

        if (GetMonitorInfo(hMonitor, ref monitorInfo))
        {
            workAreaX = monitorInfo.rcWork.Left;
            workAreaY = monitorInfo.rcWork.Top;
            workAreaWidth = monitorInfo.rcWork.Right - monitorInfo.rcWork.Left;
            workAreaHeight = monitorInfo.rcWork.Bottom - monitorInfo.rcWork.Top;
        }

        if (!GetWindowRect(hWnd, out RECT windowRect))
        {
            windowRect = new RECT { Left = 0, Top = 0, Right = 980, Bottom = 650 };
        }

        int windowWidth = windowRect.Right - windowRect.Left;
        int windowHeight = windowRect.Bottom - windowRect.Top;

        // If window has not been sized yet or is abnormally large (e.g. was maximized), size it cleanly
        if (windowWidth <= 0 || windowWidth >= workAreaWidth)
        {
            windowWidth = Math.Min(1100, (int)(workAreaWidth * 0.80));
        }
        if (windowHeight <= 0 || windowHeight >= workAreaHeight)
        {
            windowHeight = Math.Min(720, (int)(workAreaHeight * 0.85));
        }

        int posX = workAreaX + Math.Max(0, (workAreaWidth - windowWidth) / 2);
        int posY = workAreaY + Math.Max(0, (workAreaHeight - windowHeight) / 2);

        SetWindowPos(
            hWnd,
            IntPtr.Zero,
            posX,
            posY,
            windowWidth,
            windowHeight,
            SWP_NOZORDER | SWP_FRAMECHANGED | SWP_SHOWWINDOW
        );
    }

    private static void DisableMaximize(IntPtr hWnd)
    {
        // 1. Remove Maximize and Size styles from window frame
        int currentStyle = GetWindowLong(hWnd, GWL_STYLE);
        SetWindowLong(hWnd, GWL_STYLE, currentStyle & ~WS_MAXIMIZEBOX & ~WS_SIZEBOX);

        // 2. Remove Maximize and Size items from system menu (right-click / Alt+Space)
        IntPtr hMenu = GetSystemMenu(hWnd, false);
        if (hMenu != IntPtr.Zero)
        {
            DeleteMenu(hMenu, SC_MAXIMIZE, MF_BYCOMMAND);
            DeleteMenu(hMenu, SC_SIZE, MF_BYCOMMAND);
        }

        // 3. Inform Windows to immediately redraw window chrome
        SetWindowPos(
            hWnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED
        );
    }

    private static void SetWindowIcon(IntPtr hWnd)
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            }

            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                ExtractIconEx(exePath, 0, out IntPtr hIconBig, out IntPtr hIconSmall, 1);

                if (hIconSmall != IntPtr.Zero)
                {
                    SendMessage(hWnd, WM_SETICON, (IntPtr)ICON_SMALL, hIconSmall);
                }
                if (hIconBig != IntPtr.Zero)
                {
                    SendMessage(hWnd, WM_SETICON, (IntPtr)ICON_BIG, hIconBig);
                }
            }
        }
        catch
        {
            // Silently fallback if host restricts icon setting
        }
    }
}
