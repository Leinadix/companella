using System.Runtime.InteropServices;
using Companella.Services.Common;
using osu.Framework.Graphics;
using osu.Framework.Platform;

namespace Companella.Services.Screenshot;

/// <summary>
/// Service for taking screenshots of drawable components.
/// </summary>
public static class ScreenshotService
{
	// Windows API to get window position
	[DllImport("user32.dll")]
	private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

	[StructLayout(LayoutKind.Sequential)]
	private struct RECT
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	/// <summary>
	/// Captures a drawable component and copies it to the clipboard.
	/// Uses the drawable's screen position and size.
	/// </summary>
	/// <param name="drawable">The drawable to capture.</param>
	/// <param name="window">Optional window reference for accurate positioning.</param>
	public static void CaptureDrawable(Drawable drawable, IWindow? window = null)
	{
		if (drawable == null)
		{
			Logger.Info("[Screenshot] No drawable to capture");
			return;
		}

		try
		{
			// Get the drawable's position within the window
			var screenSpaceQuad = drawable.ScreenSpaceDrawQuad;

			// Get window position on screen
			var windowX = 0;
			var windowY = 0;

			if (window != null)
			{
				// Try to get window position from IWindow
				windowX = window.Position.X;
				windowY = window.Position.Y;
			}
			else
			{
				// Fallback: try to find window by common titles
				var handle = FindWindow(null, "Companella!");
				if (handle == IntPtr.Zero) handle = FindWindow(null, "Companella! - Training Mode");

				if (handle != IntPtr.Zero && GetWindowRect(handle, out var rect))
				{
					windowX = rect.Left;
					windowY = rect.Top;
				}
			}

			// Calculate actual screen coordinates
			var bounds = new Rectangle(
				windowX + (int)screenSpaceQuad.TopLeft.X,
				windowY + (int)screenSpaceQuad.TopLeft.Y,
				(int)screenSpaceQuad.Width,
				(int)screenSpaceQuad.Height
			);

			if (bounds.Width <= 0 || bounds.Height <= 0)
			{
				Logger.Info("[Screenshot] Invalid drawable bounds");
				return;
			}

			Logger.Info(
				$"[Screenshot] Window at ({windowX}, {windowY}), drawable at ({screenSpaceQuad.TopLeft.X}, {screenSpaceQuad.TopLeft.Y})");
			CaptureRegion(bounds);
		}
		catch (Exception ex)
		{
			Logger.Info($"[Screenshot] Error capturing drawable: {ex.Message}");
		}
	}

	/// <summary>
	/// Captures a specific screen region and copies it to the clipboard.
	/// </summary>
	/// <param name="region">The screen region to capture.</param>
	public static void CaptureRegion(Rectangle region)
	{
		try
		{
			Logger.Info($"[Screenshot] Capturing region: {region.X}, {region.Y}, {region.Width}x{region.Height}");

			// Create bitmap and capture screen region
			using var bitmap = new Bitmap(region.Width, region.Height);
			using var graphics = Graphics.FromImage(bitmap);

			graphics.CopyFromScreen(region.X, region.Y, 0, 0, region.Size);

			// Copy to clipboard using Windows Forms (runs on STA thread)
			CopyBitmapToClipboard(bitmap);

			Logger.Info("[Screenshot] Screenshot copied to clipboard");
		}
		catch (Exception ex)
		{
			Logger.Info($"[Screenshot] Error capturing region: {ex.Message}");
		}
	}

	/// <summary>
	/// Copies a bitmap to the Windows clipboard using a dedicated STA thread.
	/// </summary>
	private static void CopyBitmapToClipboard(Bitmap bitmap)
	{
		// Clone the bitmap since we need to pass it to another thread
		var bitmapCopy = new Bitmap(bitmap);

		// Clipboard operations require STA thread
		var thread = new Thread(() =>
		{
			try
			{
				System.Windows.Forms.Clipboard.SetImage(bitmapCopy);
				Logger.Info("[Screenshot] Clipboard.SetImage completed");
			}
			catch (Exception ex)
			{
				Logger.Info($"[Screenshot] Clipboard error: {ex.Message}");
			}
			finally
			{
				bitmapCopy.Dispose();
			}
		});

		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join(1000); // Wait up to 1 second
	}
}
