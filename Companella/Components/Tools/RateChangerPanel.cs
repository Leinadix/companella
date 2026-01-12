using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK;
using osuTK.Graphics;
using Companella.Services;
using TextBox = osu.Framework.Graphics.UserInterface.TextBox;
using Companella.Components.Session;

namespace Companella.Components.Tools;

/// <summary>
/// Modern panel for changing beatmap playback rate.
/// </summary>
public partial class RateChangerPanel : CompositeDrawable
{
    private StyledTextBox _rateTextBox = null!;
    private StyledTextBox _targetBpmTextBox = null!;
    private StyledTextBox _formatTextBox = null!;
    private ModernButton _applyButton = null!;
    private SpriteText _previewText = null!;
    private SpriteText _currentBpmLabel = null!;
    private FillFlowContainer _quickRateButtons = null!;
    private ModeToggleButton _rateModeButton = null!;
    private ModeToggleButton _bpmModeButton = null!;
    private SettingsCheckbox _pitchAdjustCheckbox = null!;
    private Container _rateInputContainer = null!;
    private Container _bpmInputContainer = null!;

    public event Action<double, string, bool>? ApplyRateClicked;
    public event Action<string>? FormatChanged;
    public event Action<double, string>? PreviewRequested;
    public event Action<bool>? PitchAdjustChanged;

    private double _currentRate = 1.0;
    private double _currentMapBpm = 120.0;
    private double _targetBpm = 120.0;
    private bool _isTargetBpmMode = false;
    private bool _pitchAdjust = true;
    private string _currentFormat = RateChanger.DefaultNameFormat;

    private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);

    public RateChangerPanel()
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 12),
                Children = new Drawable[]
                {
                    // Mode Toggle Section
                    CreateSection("Mode", new Drawable[]
                    {
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(6, 0),
                            Children = new Drawable[]
                            {
                                _rateModeButton = new ModeToggleButton("Rate", true, _accentColor)
                                {
                                    Size = new Vector2(80, 28),
                                    Action = () => SetMode(isTargetBpmMode: false),
                                    TooltipText = "Enter rate as a multiplier (e.g., 1.2x)"
                                },
                                _bpmModeButton = new ModeToggleButton("Target BPM", false, _accentColor)
                                {
                                    Size = new Vector2(100, 28),
                                    Action = () => SetMode(isTargetBpmMode: true),
                                    TooltipText = "Enter target BPM and calculate rate automatically"
                                },
                                _currentBpmLabel = new SpriteText
                                {
                                    Text = "(Current: --)",
                                    Font = new FontUsage("", 15),
                                    Colour = new Color4(100, 100, 100, 255),
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Margin = new MarginPadding { Left = 8 }
                                }
                            }
                        }
                    }),
                    // Pitch Adjust Checkbox
                    _pitchAdjustCheckbox = new SettingsCheckbox
                    {
                        LabelText = "Change Pitch",
                        IsChecked = true,
                        TooltipText = "When unchecked, preserves original pitch (nightcore-style)"
                    },
                    // Rate Selection Section (shown in Rate mode)
                    _rateInputContainer = new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Child = CreateSection("Rate", new Drawable[]
                        {
                            // Quick rate buttons
                            _quickRateButtons = new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(6, 6),
                                Children = CreateQuickRateButtons()
                            },
                            // Custom rate input
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(8, 0),
                                Margin = new MarginPadding { Top = 8 },
                                Children = new Drawable[]
                                {
                                    new SpriteText
                                    {
                                        Text = "Custom:",
                                        Font = new FontUsage("", 16),
                                        Colour = new Color4(140, 140, 140, 255),
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft
                                    },
                                    _rateTextBox = new StyledTextBox
                                    {
                                        Size = new Vector2(80, 32),
                                        PlaceholderText = "1.0"
                                    },
                                    new SpriteText
                                    {
                                        Text = "x",
                                        Font = new FontUsage("", 17),
                                        Colour = new Color4(100, 100, 100, 255),
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft
                                    }
                                }
                            }
                        })
                    },
                    // Target BPM Section (shown in Target BPM mode)
                    _bpmInputContainer = new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Alpha = 0,
                        Child = CreateSection("Target BPM", new Drawable[]
                        {
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(8, 0),
                                Children = new Drawable[]
                                {
                                    _targetBpmTextBox = new StyledTextBox
                                    {
                                        Size = new Vector2(100, 32),
                                        PlaceholderText = "120"
                                    },
                                    new SpriteText
                                    {
                                        Text = "BPM",
                                        Font = new FontUsage("", 17),
                                        Colour = new Color4(100, 100, 100, 255),
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft
                                    }
                                }
                            }
                        })
                    },
                    // Format Section
                    CreateSection("Difficulty Name Format", new Drawable[]
                    {
                        _formatTextBox = new StyledTextBox
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 32,
                            PlaceholderText = "[[name]] [[rate]]"
                        },
                        new SpriteText
                        {
                            Text = "Available: [[name]] [[rate]] [[bpm]] [[od]] [[hp]] [[msd]]",
                            Font = new FontUsage("", 17),
                            Colour = new Color4(90, 90, 90, 255),
                            Margin = new MarginPadding { Top = 4 }
                        }
                    }),
                    // Preview Section
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Children = new Drawable[]
                        {
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(8, 0),
                                Children = new Drawable[]
                                {
                                    new SpriteText
                                    {
                                        Text = "Preview:",
                                        Font = new FontUsage("", 15),
                                        Colour = new Color4(120, 120, 120, 255),
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft
                                    },
                                    _previewText = new SpriteText
                                    {
                                        Text = "...",
                                        Font = new FontUsage("", 15),
                                        Colour = _accentColor,
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft
                                    }
                                }
                            }
                        }
                    },
                    // Apply Button
                    _applyButton = new ModernButton("Create Rate-Changed Beatmap")
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 40,
                        Enabled = false,
                        TooltipText = "Create a new difficulty with modified audio speed"
                    }
                }
            }
        };

        // Set initial values
        _rateTextBox.Text = "1.0";
        _targetBpmTextBox.Text = "120";
        _formatTextBox.Text = _currentFormat;

        // Wire up events
        _rateTextBox.OnCommit += OnRateTextCommit;
        _targetBpmTextBox.OnCommit += OnTargetBpmTextCommit;
        _formatTextBox.OnCommit += OnFormatTextCommit;
        _applyButton.Clicked += OnApplyClicked;
        _pitchAdjustCheckbox.CheckedChanged += OnPitchAdjustChanged;
    }

    private void OnPitchAdjustChanged(bool isChecked)
    {
        _pitchAdjust = isChecked;
        PitchAdjustChanged?.Invoke(isChecked);
    }

    private Drawable[] CreateQuickRateButtons()
    {
        var rates = new[] { 0.5, 0.6, 0.7, 0.8, 0.9, 1.1, 1.2, 1.3, 1.4 };
        var buttons = new List<Drawable>();

        foreach (var rate in rates)
        {
            buttons.Add(new QuickRateButton(rate, rate == 1.0, _accentColor)
            {
                Size = new Vector2(42, 28),
                Action = () => SetRate(rate),
                TooltipText = $"Create a {rate:0.0#}x speed version"
            });
        }

        return buttons.ToArray();
    }

    private Container CreateSection(string title, Drawable[] content)
    {
        return new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Children = new Drawable[]
            {
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 6),
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Text = title,
                            Font = new FontUsage("", 15, "Bold"),
                            Colour = new Color4(180, 180, 180, 255)
                        },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Children = content
                        }
                    }
                }
            }
        };
    }

    /// <summary>
    /// Sets the rate value programmatically.
    /// </summary>
    public void SetRate(double rate)
    {
        _currentRate = rate;
        _rateTextBox.Text = rate.ToString("0.0#", CultureInfo.InvariantCulture);
        UpdateQuickRateButtonSelection(rate);
        UpdatePreview();
    }

    private void UpdateQuickRateButtonSelection(double selectedRate)
    {
        foreach (var child in _quickRateButtons.Children)
        {
            if (child is QuickRateButton button)
            {
                button.SetSelected(Math.Abs(button.Rate - selectedRate) < 0.001);
            }
        }
    }

    private void OnRateTextCommit(TextBox sender, bool newText)
    {
        if (double.TryParse(sender.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            _currentRate = Math.Clamp(value, 0.1, 5.0);
            sender.Text = _currentRate.ToString("0.0#", CultureInfo.InvariantCulture);
        }
        else
        {
            sender.Text = _currentRate.ToString("0.0#", CultureInfo.InvariantCulture);
        }
        UpdateQuickRateButtonSelection(_currentRate);
        UpdatePreview();
    }

    private void OnTargetBpmTextCommit(TextBox sender, bool newText)
    {
        if (double.TryParse(sender.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            _targetBpm = Math.Clamp(value, 10, 1000);
            sender.Text = _targetBpm.ToString("0.#", CultureInfo.InvariantCulture);
            
            // Calculate rate from target BPM
            if (_currentMapBpm > 0)
            {
                _currentRate = Math.Clamp(_targetBpm / _currentMapBpm, 0.1, 5.0);
            }
        }
        else
        {
            sender.Text = _targetBpm.ToString("0.#", CultureInfo.InvariantCulture);
        }
        UpdatePreview();
    }

    /// <summary>
    /// Sets the mode (Rate or Target BPM).
    /// </summary>
    private void SetMode(bool isTargetBpmMode)
    {
        if (_isTargetBpmMode == isTargetBpmMode) return;
        
        _isTargetBpmMode = isTargetBpmMode;
        
        _rateModeButton.SetSelected(!isTargetBpmMode);
        _bpmModeButton.SetSelected(isTargetBpmMode);
        
        if (isTargetBpmMode)
        {
            _rateInputContainer.FadeOut(150);
            _bpmInputContainer.FadeIn(150);
            
            // Initialize target BPM from current rate
            _targetBpm = _currentMapBpm * _currentRate;
            _targetBpmTextBox.Text = _targetBpm.ToString("0.#", CultureInfo.InvariantCulture);
        }
        else
        {
            _rateInputContainer.FadeIn(150);
            _bpmInputContainer.FadeOut(150);
        }
        
        UpdatePreview();
    }

    /// <summary>
    /// Sets the current map's BPM for target BPM calculations.
    /// </summary>
    public void SetCurrentMapBpm(double bpm)
    {
        _currentMapBpm = bpm > 0 ? bpm : 120;
        _currentBpmLabel.Text = $"(Current: {_currentMapBpm:0.#} BPM)";
        
        // If in target BPM mode, recalculate rate
        if (_isTargetBpmMode && _currentMapBpm > 0)
        {
            _currentRate = Math.Clamp(_targetBpm / _currentMapBpm, 0.1, 5.0);
            UpdatePreview();
        }
    }

    private void OnFormatTextCommit(TextBox sender, bool newText)
    {
        _currentFormat = sender.Text;
        if (string.IsNullOrWhiteSpace(_currentFormat))
        {
            _currentFormat = RateChanger.DefaultNameFormat;
            sender.Text = _currentFormat;
        }
        UpdatePreview();
        FormatChanged?.Invoke(_currentFormat);
    }

    private void UpdatePreview()
    {
        PreviewRequested?.Invoke(_currentRate, _currentFormat);
    }

    private void OnApplyClicked()
    {
        ApplyRateClicked?.Invoke(_currentRate, _currentFormat, _pitchAdjust);
    }

    /// <summary>
    /// Gets or sets whether pitch is adjusted with rate.
    /// </summary>
    public bool PitchAdjust
    {
        get => _pitchAdjust;
        set
        {
            if (_pitchAdjust == value) return;
            _pitchAdjust = value;
            if (_pitchAdjustCheckbox != null)
            {
                _pitchAdjustCheckbox.IsChecked = value;
            }
        }
    }

    public void SetPreviewText(string text)
    {
        _previewText.Text = text;
    }

    public void SetEnabled(bool enabled)
    {
        _applyButton.Enabled = enabled;
    }

    public void SetFormat(string format)
    {
        if (string.IsNullOrWhiteSpace(format))
            format = RateChanger.DefaultNameFormat;
        
        _currentFormat = format;
        _formatTextBox.Text = format;
    }

    public string CurrentFormat => _currentFormat;
}

/// <summary>
/// Quick rate selection button with selection state.
/// </summary>
public partial class QuickRateButton : CompositeDrawable, IHasTooltip
{
    public double Rate { get; }
    public Action? Action { get; set; }

    /// <summary>
    /// Tooltip text displayed on hover.
    /// </summary>
    public LocalisableString TooltipText { get; set; }

    private bool _isSelected;
    private Box _background = null!;
    private Box _hoverOverlay = null!;
    private SpriteText _label = null!;
    private readonly Color4 _accentColor;

    private readonly Color4 _normalBg = new Color4(45, 45, 50, 255);
    private readonly Color4 _selectedBg = new Color4(255, 102, 170, 255);
    private readonly Color4 _hoverBg = new Color4(60, 60, 65, 255);

    public QuickRateButton(double rate, bool isSelected, Color4 accentColor)
    {
        Rate = rate;
        _isSelected = isSelected;
        _accentColor = accentColor;
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
                Colour = _isSelected ? _selectedBg : _normalBg
            },
            _hoverOverlay = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.White,
                Alpha = 0
            },
            _label = new SpriteText
            {
                Text = $"{Rate:0.0#}x",
                Font = new FontUsage("", 12, _isSelected ? "Bold" : ""),
                Colour = _isSelected ? Color4.White : new Color4(180, 180, 180, 255),
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre
            }
        };
    }

    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        _background.FadeColour(_isSelected ? _selectedBg : _normalBg, 150);
        _label.FadeColour(_isSelected ? Color4.White : new Color4(180, 180, 180, 255), 150);
        _label.Font = new FontUsage("", 12, _isSelected ? "Bold" : "");
    }

    protected override bool OnHover(HoverEvent e)
    {
        if (!_isSelected)
        {
            _hoverOverlay.FadeTo(0.1f, 100);
            _background.FadeColour(_hoverBg, 100);
        }
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        _hoverOverlay.FadeTo(0, 100);
        if (!_isSelected)
            _background.FadeColour(_normalBg, 100);
        base.OnHoverLost(e);
    }

    protected override bool OnClick(ClickEvent e)
    {
        Action?.Invoke();
        return true;
    }
}

/// <summary>
/// Toggle button for mode selection (Rate vs Target BPM).
/// </summary>
public partial class ModeToggleButton : CompositeDrawable, IHasTooltip
{
    public Action? Action { get; set; }

    /// <summary>
    /// Tooltip text displayed on hover.
    /// </summary>
    public LocalisableString TooltipText { get; set; }

    private bool _isSelected;
    private Box _background = null!;
    private Box _hoverOverlay = null!;
    private SpriteText _label = null!;
    private readonly string _text;
    private readonly Color4 _accentColor;

    private readonly Color4 _normalBg = new Color4(45, 45, 50, 255);
    private readonly Color4 _selectedBg = new Color4(255, 102, 170, 255);
    private readonly Color4 _hoverBg = new Color4(60, 60, 65, 255);

    public ModeToggleButton(string text, bool isSelected, Color4 accentColor)
    {
        _text = text;
        _isSelected = isSelected;
        _accentColor = accentColor;
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
                Colour = _isSelected ? _selectedBg : _normalBg
            },
            _hoverOverlay = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.White,
                Alpha = 0
            },
            _label = new SpriteText
            {
                Text = _text,
                Font = new FontUsage("", 12, _isSelected ? "Bold" : ""),
                Colour = _isSelected ? Color4.White : new Color4(180, 180, 180, 255),
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre
            }
        };
    }

    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        _background.FadeColour(_isSelected ? _selectedBg : _normalBg, 150);
        _label.FadeColour(_isSelected ? Color4.White : new Color4(180, 180, 180, 255), 150);
        _label.Font = new FontUsage("", 12, _isSelected ? "Bold" : "");
    }

    protected override bool OnHover(HoverEvent e)
    {
        if (!_isSelected)
        {
            _hoverOverlay.FadeTo(0.1f, 100);
            _background.FadeColour(_hoverBg, 100);
        }
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        _hoverOverlay.FadeTo(0, 100);
        if (!_isSelected)
            _background.FadeColour(_normalBg, 100);
        base.OnHoverLost(e);
    }

    protected override bool OnClick(ClickEvent e)
    {
        Action?.Invoke();
        return true;
    }
}

/// <summary>
/// Modern styled text box with clean appearance.
/// </summary>
public partial class StyledTextBox : BasicTextBox
{
    // Use test font for Unicode character support (Greek letters, symbols)
    private static readonly FontUsage UnicodeFont = new FontUsage("Noto-Basic", 16);

    public StyledTextBox()
    {
        CornerRadius = 4;
        BackgroundFocused = new Color4(50, 50, 55, 255);
        BackgroundUnfocused = new Color4(35, 35, 40, 255);
        Masking = true;
    }

    protected override SpriteText CreatePlaceholder() => new SpriteText
    {
        Font = UnicodeFont,
        Colour = new Color4(80, 80, 80, 255)
    };

    protected override Drawable GetDrawableCharacter(char c) => new SpriteText
    {
        Text = c.ToString(),
        Font = UnicodeFont,
        Colour = Color4.White
    };
}

/// <summary>
/// Modern action button with clean styling.
/// </summary>
public partial class ModernButton : CompositeDrawable, IHasTooltip
{
    private readonly string _text;
    private Box _background = null!;
    private Box _hoverOverlay = null!;
    private SpriteText _label = null!;
    private bool _isEnabled = true;

    private Color4 _enabledColor = new Color4(255, 102, 170, 255);
    private Color4 _disabledColor = new Color4(60, 60, 65, 255);

    /// <summary>
    /// Tooltip text displayed on hover.
    /// </summary>
    public LocalisableString TooltipText { get; set; }

    public event Action? Clicked;

    public bool Enabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            if (_background != null)
            {
                _background.FadeColour(_isEnabled ? _enabledColor : _disabledColor, 150);
                _label.FadeColour(_isEnabled ? Color4.White : new Color4(100, 100, 100, 255), 150);
            }
        }
    }

    public ModernButton(string text, Color4? enabledColor = null, Color4? disabledColor = null)
    {
        _text = text;
        _enabledColor = enabledColor ?? _enabledColor;
        _disabledColor = disabledColor ?? _disabledColor;

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
                Colour = _isEnabled ? _enabledColor : _disabledColor
            },
            _hoverOverlay = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.White,
                Alpha = 0
            },
            _label = new SpriteText
            {
                Text = _text,
                Font = new FontUsage("", 17, "Bold"),
                Colour = _isEnabled ? Color4.White : new Color4(100, 100, 100, 255),
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre
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
