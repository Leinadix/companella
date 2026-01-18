using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;
using Companella.Services.Analysis;

namespace Companella.Components.Misc;

/// <summary>
/// Dialog for rating a beatmap with a dan level after completing it.
/// </summary>
public partial class DanRatingDialog : CompositeDrawable
{
    [Resolved]
    private DanConfigurationService DanConfigService { get; set; } = null!;

    private Container _dialogContainer = null!;
    private SpriteText _titleText = null!;
    private SpriteText _mapNameText = null!;
    private SpriteText _accuracyText = null!;
    private FillFlowContainer _danButtonsContainer = null!;
    private DanDialogButton _skipButton = null!;
    private SpriteText _selectedDanText = null!;
    private DanDialogButton _submitButton = null!;

    private string? _selectedDan;
    private string _beatmapHash = "";
    private string _beatmapPath = "";
    private double _accuracy;

    private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);

    /// <summary>
    /// Event raised when a dan rating is submitted.
    /// Parameters: beatmapHash, beatmapPath, danLabel, accuracy
    /// </summary>
    public event Action<string, string, string, double>? RatingSubmitted;

    /// <summary>
    /// Event raised when the dialog is closed (skipped or submitted).
    /// </summary>
    public event Action? Closed;

    public DanRatingDialog()
    {
        RelativeSizeAxes = Axes.Both;
        Alpha = 0;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            // Dim background
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(0, 0, 0, 200)
            },
            // Dialog container
            _dialogContainer = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(420, 400),
                Masking = true,
                CornerRadius = 10,
                Children = new Drawable[]
                {
                    // Background
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(30, 30, 35, 255)
                    },
                    // Content
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding(20),
                        Spacing = new Vector2(0, 12),
                        Children = new Drawable[]
                        {
                            // Title
                            _titleText = new SpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Text = "What dan is this map?",
                                Font = new FontUsage("", 22, "Bold"),
                                Colour = _accentColor
                            },
                            // Map name
                            _mapNameText = new SpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Text = "Map Name",
                                Font = new FontUsage("", 16),
                                Colour = new Color4(200, 200, 200, 255),
                                Truncate = true,
                                MaxWidth = 380
                            },
                            // Accuracy display
                            _accuracyText = new SpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Text = "Accuracy: 95.00%",
                                Font = new FontUsage("", 15),
                                Colour = new Color4(150, 150, 150, 255)
                            },
                            // Dan buttons scroll container
                            new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 180,
                                Masking = true,
                                CornerRadius = 6,
                                Children = new Drawable[]
                                {
                                    new Box
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Colour = new Color4(20, 20, 25, 255)
                                    },
                                    new BasicScrollContainer
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Padding = new MarginPadding(8),
                                        Child = _danButtonsContainer = new FillFlowContainer
                                        {
                                            RelativeSizeAxes = Axes.X,
                                            AutoSizeAxes = Axes.Y,
                                            Direction = FillDirection.Full,
                                            Spacing = new Vector2(6, 6)
                                        }
                                    }
                                }
                            },
                            // Selected dan display
                            _selectedDanText = new SpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Text = "Select a dan level",
                                Font = new FontUsage("", 16),
                                Colour = new Color4(140, 140, 140, 255)
                            },
                            // Button container
                            new FillFlowContainer
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(12, 0),
                                Margin = new MarginPadding { Top = 8 },
                                Children = new Drawable[]
                                {
                                    _skipButton = new DanDialogButton("Skip")
                                    {
                                        Size = new Vector2(100, 38),
                                        BackgroundColour = new Color4(80, 80, 85, 255)
                                    },
                                    _submitButton = new DanDialogButton("Submit")
                                    {
                                        Size = new Vector2(100, 38),
                                        BackgroundColour = _accentColor,
                                        Enabled = false
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        _skipButton.Clicked += OnSkipClicked;
        _submitButton.Clicked += OnSubmitClicked;

        // Populate dan buttons
        PopulateDanButtons();
    }

    private void PopulateDanButtons()
    {
        _danButtonsContainer.Clear();

        var labels = DanConfigService.GetAllLabels();
        foreach (var label in labels)
        {
            var button = new DanSelectButton(label)
            {
                Size = new Vector2(70, 32)
            };
            button.Clicked += () => OnDanSelected(label);
            _danButtonsContainer.Add(button);
        }
    }

    private void OnDanSelected(string dan)
    {
        _selectedDan = dan;
        _selectedDanText.Text = $"Selected: {dan}";
        _selectedDanText.Colour = _accentColor;
        _submitButton.Enabled = true;

        // Update button visuals
        foreach (var child in _danButtonsContainer.Children)
        {
            if (child is DanSelectButton btn)
            {
                btn.SetSelected(btn.DanLabel == dan);
            }
        }
    }

    /// <summary>
    /// Shows the dialog for rating a beatmap.
    /// </summary>
    public void Show(string beatmapHash, string beatmapPath, double accuracy)
    {
        _beatmapHash = beatmapHash;
        _beatmapPath = beatmapPath;
        _accuracy = accuracy;
        _selectedDan = null;

        // Update display
        var mapName = Path.GetFileNameWithoutExtension(beatmapPath);
        _mapNameText.Text = mapName;
        _accuracyText.Text = $"Accuracy: {accuracy:F2}%";
        _selectedDanText.Text = "Select a dan level";
        _selectedDanText.Colour = new Color4(140, 140, 140, 255);
        _submitButton.Enabled = false;

        // Reset button selections
        foreach (var child in _danButtonsContainer.Children)
        {
            if (child is DanSelectButton btn)
            {
                btn.SetSelected(false);
            }
        }

        // Show with animation
        this.FadeIn(200, Easing.OutQuint);
        _dialogContainer.ScaleTo(0.9f).ScaleTo(1f, 200, Easing.OutQuint);
    }

    /// <summary>
    /// Hides the dialog.
    /// </summary>
    public new void Hide()
    {
        this.FadeOut(200, Easing.OutQuint);
        Closed?.Invoke();
    }

    private void OnSkipClicked()
    {
        Hide();
    }

    private void OnSubmitClicked()
    {
        if (string.IsNullOrEmpty(_selectedDan))
            return;

        RatingSubmitted?.Invoke(_beatmapHash, _beatmapPath, _selectedDan, _accuracy);
        Hide();
    }

    protected override bool OnClick(ClickEvent e)
    {
        // Prevent clicks from passing through
        return true;
    }
}

/// <summary>
/// A styled button for the dan rating dialog.
/// </summary>
public partial class DanDialogButton : CompositeDrawable
{
    private Box _background = null!;
    private Box _hoverOverlay = null!;
    private SpriteText _textSprite = null!;
    private bool _isEnabled = true;
    private readonly string _text;

    public Color4 BackgroundColour { get; set; } = new Color4(255, 102, 170, 255);

    public event Action? Clicked;

    public bool Enabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            if (_background != null)
            {
                _background.FadeColour(_isEnabled ? BackgroundColour : new Color4(60, 60, 65, 255), 100);
            }
            if (_textSprite != null)
            {
                _textSprite.FadeColour(_isEnabled ? Color4.White : new Color4(100, 100, 100, 255), 100);
            }
        }
    }

    public DanDialogButton(string text)
    {
        _text = text;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Masking = true;
        CornerRadius = 5;

        InternalChildren = new Drawable[]
        {
            _background = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = BackgroundColour
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
                Font = new FontUsage("", 16, "Bold"),
                Colour = Color4.White
            }
        };
    }

    protected override bool OnHover(HoverEvent e)
    {
        if (_isEnabled)
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
        if (_isEnabled)
        {
            Clicked?.Invoke();
            _hoverOverlay.FadeTo(0.3f, 50).Then().FadeTo(0.15f, 100);
        }
        return true;
    }
}

/// <summary>
/// A selectable button for dan levels.
/// </summary>
public partial class DanSelectButton : CompositeDrawable
{
    private Box _background = null!;
    private Box _selectionOverlay = null!;
    private SpriteText _textSprite = null!;
    private bool _isSelected;

    public string DanLabel { get; }

    private readonly Color4 _normalBg = new Color4(45, 45, 50, 255);
    private readonly Color4 _selectedBg = new Color4(255, 102, 170, 255);

    public event Action? Clicked;

    public DanSelectButton(string danLabel)
    {
        DanLabel = danLabel;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Masking = true;
        CornerRadius = 4;

        InternalChildren = new Drawable[]
        {
            _background = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = _normalBg
            },
            _selectionOverlay = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.White,
                Alpha = 0
            },
            _textSprite = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = DanLabel,
                Font = new FontUsage("", 14, "Bold"),
                Colour = Color4.White
            }
        };
    }

    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        _background.FadeColour(selected ? _selectedBg : _normalBg, 100);
    }

    protected override bool OnHover(HoverEvent e)
    {
        if (!_isSelected)
            _selectionOverlay.FadeTo(0.15f, 100);
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        if (!_isSelected)
            _selectionOverlay.FadeTo(0, 100);
        base.OnHoverLost(e);
    }

    protected override bool OnClick(ClickEvent e)
    {
        Clicked?.Invoke();
        return true;
    }
}
