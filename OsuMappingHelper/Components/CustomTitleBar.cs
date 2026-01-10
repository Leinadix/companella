using System.Diagnostics;
using System.Runtime.InteropServices;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
using OsuMappingHelper.Services;
using osuTK;
using osuTK.Graphics;

namespace OsuMappingHelper.Components;

/// <summary>
/// Custom osu!-styled title bar with window controls.
/// </summary>
public partial class CustomTitleBar : CompositeDrawable
{
    private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);
    private readonly Color4 _backgroundColor = new Color4(20, 18, 25, 255);
    private readonly Color4 _hoverColor = new Color4(255, 130, 190, 255);
    private readonly Color4 _closeHoverColor = new Color4(255, 80, 80, 255);
    
    private IWindow? _window;
    private SpriteText _titleText = null!;
    private WindowButton _closeButton = null!;

    [Resolved]
    private GameHost Host { get; set; } = null!;

    // Windows API for native window dragging
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    private const uint WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 0x0002;

    [BackgroundDependencyLoader]
    private void load()
    {
        _window = Host.Window;
        
        RelativeSizeAxes = Axes.X;
        Height = 32;
        Anchor = Anchor.TopLeft;
        Origin = Anchor.TopLeft;

        // Create window control buttons
        _closeButton = CreateWindowButton("×", Anchor.TopRight, () =>
        {
            Schedule(() => Host.Exit());
        }, isCloseButton: true);
        _closeButton.Anchor = Anchor.TopRight;
        _closeButton.Origin = Anchor.TopRight;

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
                Padding = new MarginPadding { Left = 15, Right = 45 }, // Right padding for close button
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
                Width = 40,
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Children = new Drawable[]
                {
                    _closeButton
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

    protected override bool OnMouseDown(MouseDownEvent e)
    {
        if (e.Button == osuTK.Input.MouseButton.Left)
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
        if (_window == null) return;

        try
        {
            // Find window handle by title
            var handle = FindWindow(null, _window.Title);
            if (handle == IntPtr.Zero)
            {
                // Try to find by process
                handle = GetCurrentProcessMainWindowHandle();
            }

            if (handle != IntPtr.Zero)
            {
                // Release mouse capture and send message to start native drag
                ReleaseCapture();
                SendMessage(handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
                
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
