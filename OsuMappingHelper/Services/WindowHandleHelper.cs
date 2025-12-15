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

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

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
    /// Gets the window handle for the current process.
    /// This is a fallback method - osu!Framework should provide this.
    /// </summary>
    public static IntPtr GetCurrentProcessWindowHandle(string windowTitle)
    {
        // Try to find window by title
        var handle = FindWindowByTitle(windowTitle);
        if (handle != IntPtr.Zero)
        {
            return handle;
        }

        // Fallback: try FindWindow with null class name
        return FindWindow(null, windowTitle);
    }
}
