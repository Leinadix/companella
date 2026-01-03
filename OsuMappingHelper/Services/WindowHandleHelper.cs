using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace OsuMappingHelper.Services;

/// <summary>
/// Helper class for getting window handles on Windows.
/// </summary>
public static class WindowHandleHelper
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    private const uint GW_OWNER = 4;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // Cache the window handle to avoid repeated searches
    private static IntPtr _cachedHandle = IntPtr.Zero;
    private static int _cachedProcessId = 0;

    /// <summary>
    /// Attempts to find a window handle by title (partial match).
    /// </summary>
    public static IntPtr FindWindowByTitle(string windowTitle)
    {
        IntPtr foundHandle = IntPtr.Zero;

        EnumWindows((hWnd, lParam) =>
        {
            var sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, 256);
            var title = sb.ToString();

            if (title.Contains(windowTitle, StringComparison.OrdinalIgnoreCase))
            {
                foundHandle = hWnd;
                return false; // Stop enumeration
            }
            return true; // Continue enumeration
        }, IntPtr.Zero);

        return foundHandle;
    }

    /// <summary>
    /// Gets the main window handle for the current process.
    /// Prioritizes visible, top-level windows with the exact title match.
    /// </summary>
    public static IntPtr GetCurrentProcessWindowHandle(string windowTitle)
    {
        var currentProcessId = Process.GetCurrentProcess().Id;

        // Check if cached handle is still valid
        if (_cachedHandle != IntPtr.Zero && _cachedProcessId == currentProcessId)
        {
            // Verify the window still exists and belongs to our process
            GetWindowThreadProcessId(_cachedHandle, out uint cachedPid);
            if (cachedPid == currentProcessId && IsWindowVisible(_cachedHandle))
            {
                return _cachedHandle;
            }
            // Cache invalid, reset
            _cachedHandle = IntPtr.Zero;
        }

        IntPtr bestHandle = IntPtr.Zero;
        int bestScore = -1;

        EnumWindows((hWnd, lParam) =>
        {
            // Check if window belongs to our process
            GetWindowThreadProcessId(hWnd, out uint windowPid);
            if (windowPid != currentProcessId)
                return true; // Continue

            // Get window title
            var sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, 256);
            var title = sb.ToString();

            // Skip windows without titles (likely child/helper windows)
            if (string.IsNullOrEmpty(title))
                return true;

            // Calculate match score
            int score = 0;

            // Exact title match is best
            if (title.Equals(windowTitle, StringComparison.OrdinalIgnoreCase))
                score += 100;
            // Contains match
            else if (title.Contains(windowTitle, StringComparison.OrdinalIgnoreCase))
                score += 50;
            else
                return true; // No match, continue

            // Prefer visible windows
            if (IsWindowVisible(hWnd))
                score += 20;

            // Prefer top-level windows (no owner)
            IntPtr owner = GetWindow(hWnd, GW_OWNER);
            if (owner == IntPtr.Zero)
                score += 10;

            // Keep track of best match
            if (score > bestScore)
            {
                bestScore = score;
                bestHandle = hWnd;
            }

            return true; // Continue to find all matching windows
        }, IntPtr.Zero);

        // Cache the result
        if (bestHandle != IntPtr.Zero)
        {
            _cachedHandle = bestHandle;
            _cachedProcessId = currentProcessId;
        }

        return bestHandle;
    }

    /// <summary>
    /// Clears the cached window handle. Call this if the window is recreated.
    /// </summary>
    public static void ClearCache()
    {
        _cachedHandle = IntPtr.Zero;
        _cachedProcessId = 0;
    }
}
