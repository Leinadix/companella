using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Companella.Analyzers.Attributes;

namespace Companella.Services.Platform;

/// <summary>
/// Helper class for getting window handles on Windows.
/// </summary>
public static class WindowHandleHelper
{
	[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

	[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass,
		string? lpszWindow);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	#pragma warning disable CA1838
	private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
	#pragma warning restore CA1838

	[DllImport("user32.dll")]
	private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

	[DllImport("user32.dll")]
	private static extern bool IsWindowVisible(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
	[WinApiContext]
	private const uint GW_OWNER = 4;

	private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

	// Cache the window handle to avoid repeated searches
	private static IntPtr _cachedHandle = IntPtr.Zero;
	private static int _cachedProcessId;

	/// <summary>
	/// Attempts to find a window handle by title (partial match).
	/// </summary>
	public static IntPtr FindWindowByTitle(string windowTitle)
	{
		var foundHandle = IntPtr.Zero;

		EnumWindows((hWnd, lParam) =>
		{
			var sb = new StringBuilder(256);
			var _ =GetWindowText(hWnd, sb, 256);
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
		var currentProcessId = Environment.ProcessId;

		// Check if cached handle is still valid
		if (_cachedHandle != IntPtr.Zero && _cachedProcessId == currentProcessId)
		{
			// Verify the window still exists and belongs to our process
			var _ = GetWindowThreadProcessId(_cachedHandle, out var cachedPid);
			if (cachedPid == currentProcessId && IsWindowVisible(_cachedHandle)) return _cachedHandle;

			// Cache invalid, reset
			_cachedHandle = IntPtr.Zero;
		}

		var bestHandle = IntPtr.Zero;
		var bestScore = -1;

		EnumWindows((hWnd, lParam) =>
		{
			// Check if window belongs to our process
			var _ =GetWindowThreadProcessId(hWnd, out var windowPid);
			if (windowPid != currentProcessId)
				return true; // Continue

			// Get window title
			var sb = new StringBuilder(256);
			var __ = GetWindowText(hWnd, sb, 256);
			var title = sb.ToString();

			// Skip windows without titles (likely child/helper windows)
			if (string.IsNullOrEmpty(title))
				return true;

			// Calculate match score
			var score = 0;

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
			var owner = GetWindow(hWnd, GW_OWNER);
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
