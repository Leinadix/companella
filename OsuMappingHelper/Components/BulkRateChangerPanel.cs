using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;
using OsuMappingHelper.Services;

namespace OsuMappingHelper.Components;

/// <summary>
/// Modern panel for bulk rate changing with min/max/step inputs.
/// </summary>
public partial class BulkRateChangerPanel : CompositeDrawable
{
    private StyledTextBox _minRateTextBox = null!;
    private StyledTextBox _maxRateTextBox = null!;
    private StyledTextBox _stepTextBox = null!;
    private StyledTextBox _formatTextBox = null!;
    private ModernButton _applyButton = null!;
    private SpriteText _summaryText = null!;
    private SpriteText _ratesPreviewText = null!;
    private FillFlowContainer _presetContainer = null!;
    private SettingsCheckbox _pitchAdjustCheckbox = null!;

    public event Action<double, double, double, string, bool>? ApplyBulkRateClicked;
    public event Action<string>? FormatChanged;
    public event Action<bool>? PitchAdjustChanged;

    private double _minRate = 0.5;
    private double _maxRate = 1.5;
    private double _step = 0.1;
    private bool _pitchAdjust = true;
    private string _format = RateChanger.DefaultNameFormat;

    private const double MinRateLimit = 0.1;
    private const double MaxRateLimit = 3.0;
    private const double MinStep = 0.01;

    private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);

    public BulkRateChangerPanel()
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
                    // Presets Section
                    CreateSection("Quick Presets", new Drawable[]
                    {
                        _presetContainer = new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(8, 0),
                            Children = CreatePresetButtons()
                        }
                    }),
                    // Range Section
                    CreateSection("Rate Range", new Drawable[]
                    {
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(12, 0),
                            Children = new Drawable[]
                            {
                                CreateLabeledInput("Min", out _minRateTextBox, "0.5", 70),
                                new SpriteText
                                {
                                    Text = "to",
                                    Font = new FontUsage("", 16),
                                    Colour = new Color4(100, 100, 100, 255),
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft
                                },
                                CreateLabeledInput("Max", out _maxRateTextBox, "1.5", 70),
                                new Container { Width = 10 }, // Spacer
                                CreateLabeledInput("Step", out _stepTextBox, "0.1", 70)
                            }
                        }
                    }),
                    // Format Section
                    CreateSection("Difficulty Name Format", new Drawable[]
                    {
                        _formatTextBox = new StyledTextBox
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 32,
                            PlaceholderText = "[[name]] [[rate]]"
                        }
                    }),
                    // Pitch Adjust Checkbox
                    _pitchAdjustCheckbox = new SettingsCheckbox
                    {
                        LabelText = "Change Pitch",
                        IsChecked = true
                    },
                    // Summary Section
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Masking = true,
                        CornerRadius = 6,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = new Color4(30, 30, 35, 255)
                            },
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical,
                                Padding = new MarginPadding(12),
                                Spacing = new Vector2(0, 4),
                                Children = new Drawable[]
                                {
                                    _summaryText = new SpriteText
                                    {
                                        Text = "Will create: 11 beatmaps",
                                        Font = new FontUsage("", 16, "Bold"),
                                        Colour = _accentColor
                                    },
                                    _ratesPreviewText = new SpriteText
                                    {
                                        Text = "0.5x, 0.6x, 0.7x, ... 1.5x",
                                        Font = new FontUsage("", 14),
                                        Colour = new Color4(120, 120, 120, 255)
                                    }
                                }
                            }
                        }
                    },
                    // Apply Button
                    _applyButton = new ModernButton("Create All Beatmaps")
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 40,
                        Enabled = false
                    }
                }
            }
        };

        // Set initial values
        _formatTextBox.Text = _format;

        // Wire up events
        _minRateTextBox.OnCommit += (_, _) => OnRateInputChanged();
        _maxRateTextBox.OnCommit += (_, _) => OnRateInputChanged();
        _stepTextBox.OnCommit += (_, _) => OnRateInputChanged();
        _formatTextBox.OnCommit += (_, _) => OnFormatChanged();
        _applyButton.Clicked += OnApplyClicked;
        _pitchAdjustCheckbox.CheckedChanged += OnPitchAdjustChanged;

        UpdatePreview();
    }

    private void OnPitchAdjustChanged(bool isChecked)
    {
        _pitchAdjust = isChecked;
        PitchAdjustChanged?.Invoke(isChecked);
    }

    private Drawable[] CreatePresetButtons()
    {
        var presets = new[]
        {
            ("All in one", 0.6, 1.7, 0.05),
            ("Default", 0.8, 1.4, 0.1),
            ("Default enhanced", 0.8, 1.45, 0.05),
            ("Extreme scaling", 0.9, 1.1, 0.01)
        };

        var buttons = new List<Drawable>();
        foreach (var (label, min, max, step) in presets)
        {
            buttons.Add(new PresetButton(label, $"{min}x - {max}x")
            {
                Size = new Vector2(90, 36),
                Action = () => ApplyPreset(min, max, step)
            });
        }

        return buttons.ToArray();
    }

    private void ApplyPreset(double min, double max, double step)
    {
        _minRate = min;
        _maxRate = max;
        _step = step;
        _minRateTextBox.Text = min.ToString("0.0#", CultureInfo.InvariantCulture);
        _maxRateTextBox.Text = max.ToString("0.0#", CultureInfo.InvariantCulture);
        _stepTextBox.Text = step.ToString("0.0#", CultureInfo.InvariantCulture);
        UpdatePreview();
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

    private Container CreateLabeledInput(string label, out StyledTextBox textBox, string defaultValue, float width)
    {
        textBox = new StyledTextBox
        {
            Size = new Vector2(width, 32),
            Text = defaultValue
        };

        return new Container
        {
            AutoSizeAxes = Axes.Both,
            Child = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 2),
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Text = label,
                        Font = new FontUsage("", 13),
                        Colour = new Color4(100, 100, 100, 255)
                    },
                    textBox
                }
            }
        };
    }

    private void OnRateInputChanged()
    {
        // Parse min rate
        if (double.TryParse(_minRateTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var minVal))
        {
            _minRate = Math.Clamp(minVal, MinRateLimit, MaxRateLimit);
        }
        _minRateTextBox.Text = _minRate.ToString("0.0#", CultureInfo.InvariantCulture);

        // Parse max rate
        if (double.TryParse(_maxRateTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var maxVal))
        {
            _maxRate = Math.Clamp(maxVal, MinRateLimit, MaxRateLimit);
        }
        if (_maxRate < _minRate)
            _maxRate = _minRate;
        _maxRateTextBox.Text = _maxRate.ToString("0.0#", CultureInfo.InvariantCulture);

        // Parse step
        if (double.TryParse(_stepTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var stepVal))
        {
            _step = Math.Max(stepVal, MinStep);
        }
        _stepTextBox.Text = _step.ToString("0.0#", CultureInfo.InvariantCulture);

        UpdatePreview();
    }

    private void OnFormatChanged()
    {
        _format = _formatTextBox.Text;
        if (string.IsNullOrWhiteSpace(_format))
        {
            _format = RateChanger.DefaultNameFormat;
            _formatTextBox.Text = _format;
        }
        FormatChanged?.Invoke(_format);
    }

    public void SetFormat(string format)
    {
        if (string.IsNullOrWhiteSpace(format))
            format = RateChanger.DefaultNameFormat;
        
        _format = format;
        _formatTextBox.Text = format;
    }

    private void UpdatePreview()
    {
        var rates = CalculateRates();
        _summaryText.Text = $"Will create: {rates.Count} beatmap{(rates.Count != 1 ? "s" : "")}";

        if (rates.Count == 0)
        {
            _ratesPreviewText.Text = "No rates in range";
        }
        else if (rates.Count <= 6)
        {
            _ratesPreviewText.Text = string.Join(", ", rates.Select(r => $"{r:0.0#}x"));
        }
        else
        {
            var first = rates.Take(3).Select(r => $"{r:0.0#}x");
            var last = rates.TakeLast(2).Select(r => $"{r:0.0#}x");
            _ratesPreviewText.Text = $"{string.Join(", ", first)}, ... {string.Join(", ", last)}";
        }
    }

    private List<double> CalculateRates()
    {
        var rates = new List<double>();
        
        for (double rate = _minRate; rate < _maxRate - 0.001; rate += _step)
        {
            rates.Add(Math.Round(rate, 2));
        }
        
        if (rates.Count == 0 || Math.Abs(rates[^1] - _maxRate) > 0.001)
        {
            rates.Add(Math.Round(_maxRate, 2));
        }

        return rates;
    }

    private void OnApplyClicked()
    {
        ApplyBulkRateClicked?.Invoke(_minRate, _maxRate, _step, _format, _pitchAdjust);
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

    public void SetEnabled(bool enabled)
    {
        _applyButton.Enabled = enabled;
    }
}

/// <summary>
/// Preset button with title and subtitle.
/// </summary>
public partial class PresetButton : CompositeDrawable
{
    private readonly string _title;
    private readonly string _subtitle;
    public Action? Action { get; set; }

    private Box _background = null!;
    private Box _hoverOverlay = null!;

    private readonly Color4 _normalBg = new Color4(45, 45, 50, 255);
    private readonly Color4 _hoverBg = new Color4(55, 55, 60, 255);

    public PresetButton(string title, string subtitle)
    {
        _title = title;
        _subtitle = subtitle;
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
                Colour = _normalBg
            },
            _hoverOverlay = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.White,
                Alpha = 0
            },
            new FillFlowContainer
            {
                RelativeSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Spacing = new Vector2(0, 1),
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Text = _title,
                        Font = new FontUsage("", 15, "Bold"),
                        Colour = Color4.White,
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre
                    },
                    new SpriteText
                    {
                        Text = _subtitle,
                        Font = new FontUsage("", 12),
                        Colour = new Color4(140, 140, 140, 255),
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre
                    }
                }
            }
        };
    }

    protected override bool OnHover(HoverEvent e)
    {
        _hoverOverlay.FadeTo(0.1f, 100);
        _background.FadeColour(_hoverBg, 100);
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        _hoverOverlay.FadeTo(0, 100);
        _background.FadeColour(_normalBg, 100);
        base.OnHoverLost(e);
    }

    protected override bool OnClick(ClickEvent e)
    {
        Action?.Invoke();
        _hoverOverlay.FadeTo(0.2f, 50).Then().FadeTo(0.1f, 100);
        return true;
    }
}
