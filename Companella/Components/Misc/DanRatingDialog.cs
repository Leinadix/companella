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
    private float _selectedModifier;  // -0.33, 0, or +0.33
    private string _beatmapHash = "";
    private string _beatmapPath = "";
    private double _accuracy;

    private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);
    
    /// <summary>
    /// Converts dan label names to Greek letters where applicable.
    /// </summary>
    private static string ToGreekDisplay(string label)
    {
        return label.ToLowerInvariant() switch
        {
            "alpha" => "\u03B1",    // α
            "beta" => "\u03B2",     // β
            "gamma" => "\u03B3",    // γ
            "delta" => "\u03B4",    // δ
            "epsilon" => "\u03B5", // ε
            "zeta" => "\u03B6",     // ζ
            "eta" => "\u03B7",      // η
            "theta" => "\u03B8",    // θ
            "iota" => "\u03B9",     // ι
            "kappa" => "\u03BA",    // κ
            "lambda" => "\u03BB",   // λ
            "mu" => "\u03BC",       // μ
            "nu" => "\u03BD",       // ν
            "xi" => "\u03BE",       // ξ
            "omicron" => "\u03BF", // ο
            "pi" => "\u03C0",       // π
            "rho" => "\u03C1",      // ρ
            "sigma" => "\u03C3",    // σ
            "tau" => "\u03C4",      // τ
            "upsilon" => "\u03C5", // υ
            "phi" => "\u03C6",      // φ
            "chi" => "\u03C7",      // χ
            "psi" => "\u03C8",      // ψ
            "omega" => "\u03C9",    // ω
            _ => label
        };
    }

    /// <summary>
    /// Event raised when a dan rating is submitted.
    /// Parameters: beatmapHash, beatmapPath, danLabel, modifier (-0.33, 0, or +0.33), accuracy
    /// </summary>
    public event Action<string, string, string, float, double>? RatingSubmitted;

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
                Size = new Vector2(470, 400),
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
                                MaxWidth = 580
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
            var displayLabel = ToGreekDisplay(label);
            var group = new DanSelectGroup(label, displayLabel);
            group.Selected += (dan, modifier) => OnDanSelected(dan, modifier);
            _danButtonsContainer.Add(group);
        }
    }

    private void OnDanSelected(string dan, float modifier)
    {
        _selectedDan = dan;
        _selectedModifier = modifier;
        
        // Format display text with Greek letter
        string displayDan = ToGreekDisplay(dan);
        string modifierText = modifier switch
        {
            < 0 => "(Low)",
            > 0 => "(High)",
            _ => ""
        };
        _selectedDanText.Text = $"Selected: {displayDan} {modifierText}";
        _selectedDanText.Colour = _accentColor;
        _submitButton.Enabled = true;

        // Update button visuals
        foreach (var child in _danButtonsContainer.Children)
        {
            if (child is DanSelectGroup group)
            {
                group.SetSelected(group.DanLabel == dan, group.DanLabel == dan ? modifier : 0);
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
        _selectedModifier = 0;

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
            if (child is DanSelectGroup group)
            {
                group.SetSelected(false, 0);
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

        RatingSubmitted?.Invoke(_beatmapHash, _beatmapPath, _selectedDan, _selectedModifier, _accuracy);
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
/// A group containing [-][Dan Label][+] buttons for fine-grained dan selection.
/// </summary>
public partial class DanSelectGroup : CompositeDrawable
{
    private DanModifierButton _minusButton = null!;
    private DanSelectButton _mainButton = null!;
    private DanModifierButton _plusButton = null!;
    private bool _isSelected;
    private float _currentModifier;

    public string DanLabel { get; }
    public string DisplayLabel { get; }

    /// <summary>
    /// Event raised when a selection is made. Parameters: danLabel, modifier (-0.33, 0, or +0.33)
    /// </summary>
    public event Action<string, float>? Selected;

    public DanSelectGroup(string danLabel, string displayLabel)
    {
        DanLabel = danLabel;
        DisplayLabel = displayLabel;
        AutoSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChild = new FillFlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(2, 0),
            Children = new Drawable[]
            {
                _minusButton = new DanModifierButton("-")
                {
                    Size = new Vector2(22, 32)
                },
                _mainButton = new DanSelectButton(DisplayLabel)
                {
                    Size = new Vector2(30, 32)
                },
                _plusButton = new DanModifierButton("+")
                {
                    Size = new Vector2(22, 32)
                }
            }
        };

        _minusButton.Clicked += () => OnModifierClicked(-0.33f);
        _mainButton.Clicked += () => OnMainClicked();
        _plusButton.Clicked += () => OnModifierClicked(0.33f);
    }

    private void OnMainClicked()
    {
        _currentModifier = 0;
        Selected?.Invoke(DanLabel, 0);
    }

    private void OnModifierClicked(float modifier)
    {
        _currentModifier = modifier;
        Selected?.Invoke(DanLabel, modifier);
    }

    public void SetSelected(bool selected, float modifier)
    {
        _isSelected = selected;
        _currentModifier = modifier;
        
        _mainButton.SetSelected(selected && Math.Abs(modifier) < 0.01f);
        _minusButton.SetSelected(selected && modifier < -0.01f);
        _plusButton.SetSelected(selected && modifier > 0.01f);
    }
}

/// <summary>
/// A small button for +/- modifiers.
/// </summary>
public partial class DanModifierButton : CompositeDrawable
{
    private Box _background = null!;
    private Box _selectionOverlay = null!;
    private SpriteText _textSprite = null!;
    private bool _isSelected;
    private readonly string _text;

    private readonly Color4 _normalBg = new Color4(35, 35, 40, 255);
    private readonly Color4 _selectedBg = new Color4(200, 80, 140, 255);

    public event Action? Clicked;

    public DanModifierButton(string text)
    {
        _text = text;
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
                Text = _text,
                Font = new FontUsage("", 16, "Bold"),
                Colour = new Color4(180, 180, 180, 255)
            }
        };
    }

    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        _background.FadeColour(selected ? _selectedBg : _normalBg, 100);
        _textSprite.FadeColour(selected ? Color4.White : new Color4(180, 180, 180, 255), 100);
    }

    protected override bool OnHover(HoverEvent e)
    {
        if (!_isSelected)
            _selectionOverlay.FadeTo(0.2f, 100);
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
        _selectionOverlay.FadeTo(0.4f, 50).Then().FadeTo(_isSelected ? 0 : 0.2f, 100);
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
