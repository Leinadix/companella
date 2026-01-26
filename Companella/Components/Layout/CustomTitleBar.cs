using System.Diagnostics;
using System.Runtime.InteropServices;
using Companella.Analyzers.Attributes;
using Companella.Services.Common;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
using osuTK;
using osuTK.Graphics;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;

namespace Companella.Components.Layout;

/// <summary>
/// Custom osu!-styled title bar with window controls.
/// </summary>
public partial class CustomTitleBar : CompositeDrawable
{
	private readonly Color4 _accentColor = new(255, 102, 170, 255);
	private readonly Color4 _backgroundColor = new(20, 18, 25, 255);
	private readonly Color4 _hoverColor = new(255, 130, 190, 255);
	private readonly Color4 _closeHoverColor = new(255, 80, 80, 255);

	private IWindow? _window;
	private SpriteText _titleText = null!;
	private WindowButton _closeButton = null!;
	private WindowButton _cameraButton = null!;
	private bool _isDraggable = true;
	private Action? _customCloseAction;

	/// <summary>
	/// Event raised when the camera/screenshot button is clicked.
	/// </summary>
	public event Action? ScreenshotRequested;

	/// <summary>
	/// Event raised when the close button is clicked (if no custom close action is set).
	/// </summary>
	public event Action? CloseRequested;

	[Resolved] private GameHost Host { get; set; } = null!;

	[Resolved] private IRenderer Renderer { get; set; } = null!;

	// Windows API for native window dragging
	[DllImport("user32.dll")]
	private static extern bool ReleaseCapture();

	[DllImport("user32.dll")]
	private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

	[WinApiContext]
	private const uint WM_NCLBUTTONDOWN = 0x00A1;
	[WinApiContext]
	private const int HT_CAPTION = 0x0002;

	[BackgroundDependencyLoader]
	private void load()
	{
		_window = Host.Window;

		RelativeSizeAxes = Axes.X;
		Height = 32;
		Anchor = Anchor.TopLeft;
		Origin = Anchor.TopLeft;

		// Create window control buttons
		_closeButton = CreateWindowButton("\u00D7", Anchor.TopRight, () =>
		{
			if (_customCloseAction != null)
				_customCloseAction();
			else if (CloseRequested != null)
				CloseRequested();
			else
				Schedule(() => Host.Exit());
		}, true);
		_closeButton.Anchor = Anchor.TopRight;
		_closeButton.Origin = Anchor.TopRight;

		// Create camera/screenshot button with icon
		_cameraButton = CreateCameraButton(() => ScreenshotRequested?.Invoke());
		_cameraButton.Anchor = Anchor.TopRight;
		_cameraButton.Origin = Anchor.TopRight;

		InternalChildren = new Drawable[]
		{
			// Background with accent line at bottom
			new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = _backgroundColor
			},
			new Box
			{
				RelativeSizeAxes = Axes.X,
				Height = 2,
				Anchor = Anchor.BottomLeft,
				Origin = Anchor.BottomLeft,
				Colour = _accentColor,
				Alpha = 0.6f
			},
			// Title text
			new Container
			{
				RelativeSizeAxes = Axes.Both,
				Padding = new MarginPadding { Left = 15, Right = 85 }, // Right padding for buttons
				Child = _titleText = new SpriteText
				{
					Anchor = Anchor.CentreLeft,
					Origin = Anchor.CentreLeft,
					Text = "Companella!",
					Font = new FontUsage("", 16, "SemiBold"),
					Colour = new Color4(230, 230, 230, 255)
				}
			},
			// Window control buttons (right side)
			new Container
			{
				RelativeSizeAxes = Axes.Y,
				Width = 80,
				Anchor = Anchor.TopRight,
				Origin = Anchor.TopRight,
				Children = new Drawable[]
				{
					_closeButton,
					new Container
					{
						RelativeSizeAxes = Axes.Y,
						Width = 40,
						Anchor = Anchor.TopRight,
						Origin = Anchor.TopRight,
						Margin = new MarginPadding { Right = 40 },
						Child = _cameraButton
					}
				}
			}
		};
	}

	private WindowButton CreateWindowButton(string text, Anchor anchor, Action onClick, bool isCloseButton = false)
	{
		return new WindowButton(isCloseButton ? _closeHoverColor : _hoverColor, onClick)
		{
			Size = new Vector2(40, 32),
			Anchor = anchor,
			Origin = anchor,
			Child = new SpriteText
			{
				Anchor = Anchor.Centre,
				Origin = Anchor.Centre,
				Text = text,
				Font = new FontUsage("", isCloseButton ? 20 : 18, "Bold"),
				Colour = new Color4(200, 200, 200, 255)
			}
		};
	}

	private WindowButton CreateCameraButton(Action onClick)
	{
		var button = new WindowButton(_hoverColor, onClick)
		{
			Size = new Vector2(40, 32),
			Anchor = Anchor.TopRight,
			Origin = Anchor.TopRight
		};

		// Load camera icon from embedded resources
		try
		{
			var assembly = typeof(CustomTitleBar).Assembly;
			var resourceStream = assembly.GetManifestResourceStream("Companella.Resources.Images.e722.png");

			if (resourceStream != null)
				using (resourceStream)
				{
					var image = Image.Load<Rgba32>(resourceStream);
					var texture = Renderer.CreateTexture(image.Width, image.Height);
					texture.SetData(new TextureUpload(image));

					button.Child = new Sprite
					{
						Texture = texture,
						Size = new Vector2(18, 18),
						Anchor = Anchor.Centre,
						Origin = Anchor.Centre,
						Colour = new Color4(200, 200, 200, 255)
					};
				}
			else
				// Fallback to text if image not found
				button.Child = new SpriteText
				{
					Anchor = Anchor.Centre,
					Origin = Anchor.Centre,
					Text = "Cam",
					Font = new FontUsage("", 12, "Bold"),
					Colour = new Color4(200, 200, 200, 255)
				};
		}
		catch (Exception ex)
		{
			Logger.Info($"[TitleBar] Error loading camera icon: {ex.Message}");
			// Fallback to text
			button.Child = new SpriteText
			{
				Anchor = Anchor.Centre,
				Origin = Anchor.Centre,
				Text = "Cam",
				Font = new FontUsage("", 12, "Bold"),
				Colour = new Color4(200, 200, 200, 255)
			};
		}

		return button;
	}

	/// <summary>
	/// Sets the title text.
	/// </summary>
	public void SetTitle(string title)
	{
		if (_titleText != null)
			_titleText.Text = title;
	}

	/// <summary>
	/// Sets whether the title bar allows window dragging.
	/// </summary>
	public void SetDraggable(bool draggable)
	{
		_isDraggable = draggable;
	}

	/// <summary>
	/// Sets a custom action for the close button.
	/// </summary>
	public void SetCloseAction(Action? action)
	{
		_customCloseAction = action;
	}

	protected override bool OnMouseDown(MouseDownEvent e)
	{
		if (e.Button == osuTK.Input.MouseButton.Left && _isDraggable)
		{
			// Start native Windows window drag
			StartWindowDrag();
			return true;
		}

		return base.OnMouseDown(e);
	}

	/// <summary>
	/// Initiates native Windows window dragging.
	/// </summary>
	private void StartWindowDrag()
	{
		if (_window == null)
			return;

		try
		{
			// Find window handle by title
			var handle = FindWindow(null, _window.Title);
			if (handle == IntPtr.Zero)
				// Try to find by process
				handle = GetCurrentProcessMainWindowHandle();

			if (handle != IntPtr.Zero)
			{
				// Release mouse capture and send message to start native drag
				ReleaseCapture();
				SendMessage(handle, WM_NCLBUTTONDOWN, (IntPtr)HT_CAPTION, IntPtr.Zero);
			}
		}
		catch (Exception ex)
		{
			Logger.Info($"[TitleBar] Error starting window drag: {ex.Message}");
		}
	}

	/// <summary>
	/// Gets the main window handle of the current process.
	/// </summary>
	private static IntPtr GetCurrentProcessMainWindowHandle()
	{
		try
		{
			using var process = Process.GetCurrentProcess();
			return process.MainWindowHandle;
		}
		catch
		{
			return IntPtr.Zero;
		}
	}
}

/// <summary>
/// A clickable button for window controls with hover effects.
/// </summary>
public partial class WindowButton : CompositeDrawable
{
	private readonly Color4 _hoverColor;
	private readonly Action _onClick;
	private Box _hoverOverlay = null!;
	private Drawable? _content;

	public WindowButton(Color4 hoverColor, Action onClick)
	{
		_hoverColor = hoverColor;
		_onClick = onClick;
	}

	public Drawable? Child
	{
		get => _content;
		set
		{
			if (_content != null)
				RemoveInternal(_content, false);
			_content = value;
			if (_content != null && IsLoaded)
				AddInternal(_content);
		}
	}

	[BackgroundDependencyLoader]
	private void load()
	{
		Masking = true;
		InternalChildren = new Drawable[]
		{
			new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = Color4.Transparent
			},
			_hoverOverlay = new Box
			{
				RelativeSizeAxes = Axes.Both,
				Colour = _hoverColor,
				Alpha = 0
			}
		};

		if (_content != null)
			AddInternal(_content);
	}

	protected override bool OnHover(HoverEvent e)
	{
		_hoverOverlay.FadeTo(0.3f, 100);
		return base.OnHover(e);
	}

	protected override void OnHoverLost(HoverLostEvent e)
	{
		_hoverOverlay.FadeTo(0, 100);
		base.OnHoverLost(e);
	}

	protected override bool OnClick(ClickEvent e)
	{
		_onClick?.Invoke();
		return true;
	}
}
