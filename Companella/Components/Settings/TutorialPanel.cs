using System.Diagnostics;
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
/// Panel that allows users to re-run the tutorial and quick setup.
/// </summary>
public partial class TutorialPanel : CompositeDrawable
{
    private TutorialPanelButton _showTutorialButton = null!;
    private TutorialPanelButton _quickSetupButton = null!;

    /// <summary>
    /// Event raised when the Show Tutorial button is clicked.
    /// </summary>
    public event Action? ShowTutorialRequested;

    /// <summary>
    /// Event raised when the Quick Setup button is clicked.
    /// </summary>
    public event Action? QuickSetupRequested;

    private readonly Color4 _panelBgColor = new Color4(30, 30, 35, 255);
    private readonly Color4 _borderColor = new Color4(50, 50, 55, 255);
    private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);

    public TutorialPanel()
    {
        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            new BasicScrollContainer
            {
                RelativeSizeAxes = Axes.Both,
                ClampExtension = 100,
                Child = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 20),
                    Padding = new MarginPadding(20),
                    Children = new Drawable[]
                    {
                        // Tutorial Section
                        CreateSection(
                            "App Tutorial",
                            "New to Companella? The tutorial walks you through all the main features " +
                            "including rate changing, session tracking, skills analysis, and mapping tools.",
                            "Show Tutorial",
                            _accentColor,
                            out _showTutorialButton
                        ),
                        // Quick Setup Section
                        CreateSection(
                            "Quick Setup",
                            "Run all initial setup tasks at once:\n\n" +
                            "- Index Beatmaps: Scans your osu! songs folder and calculates MSD ratings for recommendations\n" +
                            "- Import Scores: Imports your existing osu! scores as session plays\n" +
                            "- Find Replays: Links replay files to your session plays for detailed analysis\n\n" +
                            "This may take several minutes depending on your library size.",
                            "Run Quick Setup",
                            new Color4(80, 180, 80, 255),
                            out _quickSetupButton
                        ),
                        // Help Section with links
                        CreateHelpSection()
                    }
                }
            }
        };

        _showTutorialButton.Clicked += () => ShowTutorialRequested?.Invoke();
        _quickSetupButton.Clicked += () => QuickSetupRequested?.Invoke();
    }

    private Container CreateSection(string title, string description, string buttonText, Color4 buttonColor, out TutorialPanelButton button)
    {
        button = new TutorialPanelButton(buttonText, buttonColor)
        {
            Size = new Vector2(180, 40)
        };

        return new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Masking = true,
            CornerRadius = 8,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = _panelBgColor
                },
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
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Padding = new MarginPadding(20),
                    Spacing = new Vector2(0, 12),
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Text = title,
                            Font = new FontUsage("", 20, "Bold"),
                            Colour = _accentColor
                        },
                        new TextFlowContainer(s =>
                        {
                            s.Font = new FontUsage("", 14);
                            s.Colour = new Color4(200, 200, 200, 255);
                        })
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Text = description
                        },
                        new Container
                        {
                            AutoSizeAxes = Axes.Both,
                            Padding = new MarginPadding { Top = 8 },
                            Child = button
                        }
                    }
                }
            }
        };
    }

    private Container CreateInfoSection(string title, string description)
    {
        return new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Masking = true,
            CornerRadius = 8,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = _panelBgColor
                },
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
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Padding = new MarginPadding(20),
                    Spacing = new Vector2(0, 12),
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Text = title,
                            Font = new FontUsage("", 20, "Bold"),
                            Colour = _accentColor
                        },
                        new TextFlowContainer(s =>
                        {
                            s.Font = new FontUsage("", 14);
                            s.Colour = new Color4(180, 180, 180, 255);
                        })
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Text = description
                        }
                    }
                }
            }
        };
    }

    private Container CreateHelpSection()
    {
        return new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Masking = true,
            CornerRadius = 8,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = _panelBgColor
                },
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
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Padding = new MarginPadding(20),
                    Spacing = new Vector2(0, 12),
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Text = "Need Help?",
                            Font = new FontUsage("", 20, "Bold"),
                            Colour = _accentColor
                        },
                        new TextFlowContainer(s =>
                        {
                            s.Font = new FontUsage("", 14);
                            s.Colour = new Color4(180, 180, 180, 255);
                        })
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Text = "If you encounter any issues or have questions:"
                        },
                        // Links section
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 8),
                            Children = new Drawable[]
                            {
                                CreateLinkRow("Check the GitHub repository for documentation and issues"),
                                CreateClickableLinkRow("Visit the ", "osu! Forum Thread", "https://osu.ppy.sh/community/forums/topics/2168176", " to ask questions"),
                                CreateLinkRow("Report bugs on GitHub Issues")
                            }
                        },
                        // Discord button
                        new Container
                        {
                            AutoSizeAxes = Axes.Both,
                            Padding = new MarginPadding { Top = 8 },
                            Child = new TutorialPanelButton("Join the Discord", new Color4(88, 101, 242, 255))
                            {
                                Size = new Vector2(180, 40),
                                Action = () =>
                                {
                                    try
                                    {
                                        Process.Start(new ProcessStartInfo
                                        {
                                            FileName = "https://discord.gg/4xsku7y896",
                                            UseShellExecute = true
                                        });
                                    }
                                    catch { }
                                }
                            }
                        }
                    }
                }
            }
        };
    }

    private static FillFlowContainer CreateLinkRow(string text)
    {
        return new FillFlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(4, 0),
            Children = new Drawable[]
            {
                new SpriteText
                {
                    Text = "- " + text,
                    Font = new FontUsage("", 14),
                    Colour = new Color4(180, 180, 180, 255)
                }
            }
        };
    }

    private static FillFlowContainer CreateClickableLinkRow(string prefix, string linkText, string url, string suffix)
    {
        return new FillFlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(0, 0),
            Children = new Drawable[]
            {
                new SpriteText
                {
                    Text = "- " + prefix,
                    Font = new FontUsage("", 14),
                    Colour = new Color4(180, 180, 180, 255)
                },
                new ClickableLinkText(linkText, url)
                {
                    Font = new FontUsage("", 14),
                    Colour = new Color4(255, 102, 170, 255)
                },
                new SpriteText
                {
                    Text = suffix,
                    Font = new FontUsage("", 14),
                    Colour = new Color4(180, 180, 180, 255)
                }
            }
        };
    }
}

/// <summary>
/// A simple clickable link text.
/// </summary>
public partial class ClickableLinkText : SpriteText
{
    private readonly string _url;

    public ClickableLinkText(string text, string url)
    {
        _url = url;
        Text = text;
    }

    protected override bool OnHover(HoverEvent e)
    {
        this.FadeColour(new Color4(255, 150, 200, 255), 100);
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        this.FadeColour(new Color4(255, 102, 170, 255), 100);
        base.OnHoverLost(e);
    }

    protected override bool OnClick(ClickEvent e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _url,
                UseShellExecute = true
            });
        }
        catch { }
        return true;
    }
}

/// <summary>
/// Styled button for the tutorial panel.
/// </summary>
public partial class TutorialPanelButton : CompositeDrawable
{
    private Box _background = null!;
    private Box _hoverOverlay = null!;
    private readonly string _text;
    private readonly Color4 _buttonColor;

    public event Action? Clicked;
    
    /// <summary>
    /// Action to execute when clicked. Alternative to subscribing to Clicked event.
    /// </summary>
    public Action? Action { get; set; }

    public TutorialPanelButton(string text, Color4 buttonColor)
    {
        _text = text;
        _buttonColor = buttonColor;
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
            new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = _text,
                Font = new FontUsage("", 15, "Bold"),
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
        Action?.Invoke();
        _hoverOverlay.FadeTo(0.3f, 50).Then().FadeTo(0.15f, 100);
        return true;
    }
}
