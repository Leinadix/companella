using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Companella.Analyzers.Attributes;
using Companella.Services.Common;

namespace Companella;

/// <summary>
/// Native Windows splash form that shows immediately on startup, before osu!framework loads.
/// Uses per-pixel alpha for true transparency.
/// </summary>
public class SplashForm : Form
{
	private Bitmap? _logoBitmap;
	private Bitmap? _compositeBitmap;
	private System.Windows.Forms.Timer? _animationTimer;
	private float _currentAlpha;
	private float _currentScale = 0.7f;
	private bool _isFadingIn = true;
	private bool _isFadingOut;
	private bool _isClosing;

	private const float _fadeInSpeed = 0.08f;
	private const float _fadeOutSpeed = 0.12f;
	private const float _scaleInSpeed = 0.02f;
	private const float _targetScale = 1.0f;
	private const int _logoSize = 200;
	private const int _animationInterval = 16; // ~60fps

	public SplashForm()
	{
		// Configure form for transparency
		FormBorderStyle = FormBorderStyle.None;
		StartPosition = FormStartPosition.CenterScreen;
		ShowInTaskbar = false;
		TopMost = true;
		Size = new Size(300, 300);

		// Load and prepare the logo
		LoadLogo();

		// Setup animation timer
		_animationTimer = new System.Windows.Forms.Timer();
		_animationTimer.Interval = _animationInterval;
		_animationTimer.Tick += OnAnimationTick;
	}

	private void LoadLogo()
	{
		try
		{
			var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");

			if (!File.Exists(iconPath)) iconPath = "icon.ico";

			if (File.Exists(iconPath))
			{
				using var icon = new Icon(iconPath);
				_logoBitmap = icon.ToBitmap();
			}
		}
		catch (Exception ex)
		{
			Logger.Info($"[SplashForm] Failed to load logo: {ex.Message}");
		}
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		_animationTimer?.Start();
	}

	private void OnAnimationTick(object? sender, EventArgs e)
	{
		if (_isFadingIn)
		{
			_currentAlpha += _fadeInSpeed;
			_currentScale += _scaleInSpeed;

			if (_currentAlpha >= 1f)
			{
				_currentAlpha = 1f;
				_isFadingIn = false;
			}

			if (_currentScale >= _targetScale) _currentScale = _targetScale;

			UpdateLayeredWindow();
		}
		else if (_isFadingOut)
		{
			_currentAlpha -= _fadeOutSpeed;
			_currentScale += _scaleInSpeed * 0.3f; // Slight scale up while fading out

			if (_currentAlpha <= 0f)
			{
				_currentAlpha = 0f;
				_isFadingOut = false;
				_isClosing = true;
				_animationTimer?.Stop();
				Close();
			}
			else
			{
				UpdateLayeredWindow();
			}
		}
	}

	/// <summary>
	/// Starts the fade out animation and closes the form when complete.
	/// </summary>
	public void FadeOutAndClose()
	{
		if (_isClosing || _isFadingOut)
			return;

		_isFadingOut = true;
		_isFadingIn = false;
	}

	private void UpdateLayeredWindow()
	{
		if (_logoBitmap == null)
			return;

		// Create composite bitmap with current animation state
		_compositeBitmap?.Dispose();
		_compositeBitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);

		using (var g = Graphics.FromImage(_compositeBitmap))
		{
			g.SmoothingMode = SmoothingMode.HighQuality;
			g.InterpolationMode = InterpolationMode.HighQualityBicubic;
			g.PixelOffsetMode = PixelOffsetMode.HighQuality;
			g.Clear(Color.Transparent);

			// Calculate scaled size and position
			var scaledSize = (int)(_logoSize * _currentScale);
			var x = (Width - scaledSize) / 2;
			var y = (Height - scaledSize) / 2;

			// Create color matrix for alpha
			var colorMatrix = new ColorMatrix();
			colorMatrix.Matrix33 = _currentAlpha;

			var imageAttributes = new ImageAttributes();
			imageAttributes.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

			// Draw the logo with current alpha and scale
			g.DrawImage(
				_logoBitmap,
				new Rectangle(x, y, scaledSize, scaledSize),
				0, 0, _logoBitmap.Width, _logoBitmap.Height,
				GraphicsUnit.Pixel,
				imageAttributes);
		}

		// Apply the layered window
		SetBitmap(_compositeBitmap);
	}

	/// <summary>
	/// Sets the bitmap for a layered window with per-pixel alpha.
	/// </summary>
	private void SetBitmap(Bitmap bitmap)
	{
		if (!IsHandleCreated)
			return;

		var screenDc = GetDC(IntPtr.Zero);
		var memDc = CreateCompatibleDC(screenDc);
		var hBitmap = IntPtr.Zero;
		var oldBitmap = IntPtr.Zero;

		try
		{
			hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
			oldBitmap = SelectObject(memDc, hBitmap);

			var size = new SIZE(bitmap.Width, bitmap.Height);
			var pointSource = new POINT(0, 0);
			var topPos = new POINT(Left, Top);
			var blend = new BLENDFUNCTION
			{
				BlendOp = AC_SRC_OVER,
				BlendFlags = 0,
				SourceConstantAlpha = 255,
				AlphaFormat = AC_SRC_ALPHA
			};

			UpdateLayeredWindow(Handle, screenDc, ref topPos, ref size, memDc, ref pointSource, 0, ref blend,
				ULW_ALPHA);
		}
		finally
		{
			var _ = ReleaseDC(IntPtr.Zero, screenDc);
			if (hBitmap != IntPtr.Zero)
			{
				SelectObject(memDc, oldBitmap);
				DeleteObject(hBitmap);
			}

			DeleteDC(memDc);
		}
	}

	protected override CreateParams CreateParams
	{
		get
		{
			var cp = base.CreateParams;
			cp.ExStyle |= WS_EX_LAYERED;
			return cp;
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_animationTimer?.Stop();
			_animationTimer?.Dispose();
			_logoBitmap?.Dispose();
			_compositeBitmap?.Dispose();
		}

		base.Dispose(disposing);
	}

	#region Native Methods and Constants
	[WinApiContext]
	private const int WS_EX_LAYERED = 0x80000;
	[WinApiContext]
	private const int ULW_ALPHA = 0x02;
	[WinApiContext]
	private const byte AC_SRC_OVER = 0x00;
	[WinApiContext]
	private const byte AC_SRC_ALPHA = 0x01;

	[StructLayout(LayoutKind.Sequential)]
	private struct POINT
	{
		public int X;
		public int Y;

		public POINT(int x, int y)
		{
			X = x;
			Y = y;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct SIZE
	{
		public int Width;
		public int Height;

		public SIZE(int width, int height)
		{
			Width = width;
			Height = height;
		}
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	private struct BLENDFUNCTION
	{
		public byte BlendOp;
		public byte BlendFlags;
		public byte SourceConstantAlpha;
		public byte AlphaFormat;
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
		IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern IntPtr GetDC(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

	[DllImport("gdi32.dll", SetLastError = true)]
	private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

	[DllImport("gdi32.dll", SetLastError = true)]
	private static extern bool DeleteDC(IntPtr hdc);

	[DllImport("gdi32.dll")]
	private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

	[DllImport("gdi32.dll", SetLastError = true)]
	private static extern bool DeleteObject(IntPtr hObject);

	#endregion
}
