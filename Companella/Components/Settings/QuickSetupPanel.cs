using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Settings;

/// <summary>
/// Panel for quick setup that indexes maps, imports scores, and finds missing replays.
/// </summary>
public partial class QuickSetupPanel : CompositeDrawable
{
    private QuickSetupButton _quickSetupButton = null!;
    private SpriteText _statusText = null!;

    /// <summary>
    /// Event raised when the Quick Setup button is clicked.
    /// </summary>
    public event Action? QuickSetupRequested;

    private readonly Color4 _panelBgColor = new Color4(30, 30, 35, 255);
    private readonly Color4 _borderColor = new Color4(50, 50, 55, 255);
    private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);

    public QuickSetupPanel()
    {
        AutoSizeAxes = Axes.Y;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Masking = true,
                CornerRadius = 8,
                Children = new Drawable[]
                {
                    // Background
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = _panelBgColor
                    },
                    // Border
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = 8,
                        BorderColour = _borderColor,
                        BorderThickness = 1f,
                        Child = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Alpha = 0,
                            AlwaysPresent = true
                        }
                    },
                    // Content
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding(16),
                        Spacing = new Vector2(0, 12),
                        Children = new Drawable[]
                        {
                            // Title
                            new SpriteText
                            {
                                Text = "Quick Setup",
                                Font = new FontUsage("", 18, "Bold"),
                                Colour = _accentColor
                            },
                            // Description
                            new TextFlowContainer(s =>
                            {
                                s.Font = new FontUsage("", 14);
                                s.Colour = new Color4(180, 180, 180, 255);
                            })
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Text = "Run all setup tasks at once: Index beatmaps for recommendations, " +
                                       "import existing scores as sessions, and find missing replay files."
                            },
                            // Button and status row
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(16, 0),
                                Children = new Drawable[]
                                {
                                    _quickSetupButton = new QuickSetupButton("Run Quick Setup")
                                    {
                                        Size = new Vector2(160, 36)
                                    },
                                    _statusText = new SpriteText
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Font = new FontUsage("", 13),
                                        Colour = new Color4(150, 150, 150, 255),
                                        Alpha = 0
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        _quickSetupButton.Clicked += OnQuickSetupClicked;
    }

    private void OnQuickSetupClicked()
    {
        QuickSetupRequested?.Invoke();
    }

    /// <summary>
    /// Updates the status text displayed next to the button.
    /// </summary>
    public void SetStatus(string status)
    {
        Schedule(() =>
        {
            _statusText.Text = status;
            _statusText.Alpha = string.IsNullOrEmpty(status) ? 0 : 1;
        });
    }
}

/// <summary>
/// Styled button for quick setup.
/// </summary>
public partial class QuickSetupButton : CompositeDrawable
{
    private Box _background = null!;
    private Box _hoverOverlay = null!;
    private SpriteText _textSprite = null!;
    private readonly string _text;

    private readonly Color4 _buttonColor = new Color4(80, 180, 80, 255);

    public event Action? Clicked;

    public QuickSetupButton(string text)
    {
        _text = text;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Masking = true;
        CornerRadius = 6;

        InternalChildren = new Drawable[]
        {
            _background = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = _buttonColor
            },
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 6,
                BorderColour = new Color4(255, 255, 255, 30),
                BorderThickness = 1f,
                Child = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Alpha = 0,
                    AlwaysPresent = true
                }
            },
            _hoverOverlay = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.White,
                Alpha = 0
            },
            _textSprite = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = _text,
                Font = new FontUsage("", 14, "Bold"),
                Colour = Color4.White
            }
        };
    }

    protected override bool OnHover(HoverEvent e)
    {
        _hoverOverlay.FadeTo(0.15f, 100);
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        _hoverOverlay.FadeTo(0, 100);
        base.OnHoverLost(e);
    }

    protected override bool OnClick(ClickEvent e)
    {
        Clicked?.Invoke();
        _hoverOverlay.FadeTo(0.3f, 50).Then().FadeTo(0.15f, 100);
        return true;
    }
}
