using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osuTK;
using osuTK.Graphics;
using OsuMappingHelper.Services;

namespace OsuMappingHelper.Components;

/// <summary>
/// Panel for bulk rate changing with min/max/step inputs.
/// </summary>
public partial class BulkRateChangerPanel : CompositeDrawable
{
    private BasicTextBox _minRateTextBox = null!;
    private BasicTextBox _maxRateTextBox = null!;
    private BasicTextBox _stepTextBox = null!;
    private BasicTextBox _formatTextBox = null!;
    private FunctionButton _applyButton = null!;
    private SpriteText _previewText = null!;
    private SpriteText _countText = null!;

    public event Action<double, double, double, string>? ApplyBulkRateClicked;

    private double _minRate = 0.5;
    private double _maxRate = 1.5;
    private double _step = 0.1;
    private string _format = RateChanger.DefaultNameFormat;

    private const double MIN_RATE_LIMIT = 0.1;
    private const double MAX_RATE_LIMIT = 3.0;
    private const double MIN_STEP = 0.01;

    private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(40, 40, 48, 255)
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding(15),
                        Child = new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 8),
                            Children = new Drawable[]
                            {
                                new SpriteText
                                {
                                    Text = "Bulk Rate Changer",
                                    Font = new FontUsage("", 16, "Bold"),
                                    Colour = _accentColor
                                },
                                // Rate range row
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
                                            Text = "Range:",
                                            Font = new FontUsage("", 14),
                                            Colour = new Color4(200, 200, 200, 255),
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft,
                                            Width = 50
                                        },
                                        CreateTextBox(out _minRateTextBox, "0.5", 60),
                                        new SpriteText
                                        {
                                            Text = "x to",
                                            Font = new FontUsage("", 14),
                                            Colour = new Color4(150, 150, 150, 255),
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft
                                        },
                                        CreateTextBox(out _maxRateTextBox, "1.5", 60),
                                        new SpriteText
                                        {
                                            Text = "x",
                                            Font = new FontUsage("", 14),
                                            Colour = new Color4(150, 150, 150, 255),
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft
                                        },
                                        new SpriteText
                                        {
                                            Text = "Step:",
                                            Font = new FontUsage("", 14),
                                            Colour = new Color4(200, 200, 200, 255),
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft,
                                            Margin = new MarginPadding { Left = 15 }
                                        },
                                        CreateTextBox(out _stepTextBox, "0.1", 60),
                                    }
                                },
                                // Quick presets row
                                new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Direction = FillDirection.Horizontal,
                                    Spacing = new Vector2(5, 0),
                                    Children = new Drawable[]
                                    {
                                        new SpriteText
                                        {
                                            Text = "Presets:",
                                            Font = new FontUsage("", 12),
                                            Colour = new Color4(150, 150, 150, 255),
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft
                                        },
                                        CreatePresetButton("0.5-1.0", 0.5, 1.0, 0.1),
                                        CreatePresetButton("0.8-1.2", 0.8, 1.2, 0.05),
                                        CreatePresetButton("1.0-1.5", 1.0, 1.5, 0.1),
                                        CreatePresetButton("1.0-2.0", 1.0, 2.0, 0.1),
                                    }
                                },
                                // Format input row
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
                                            Text = "Format:",
                                            Font = new FontUsage("", 14),
                                            Colour = new Color4(200, 200, 200, 255),
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft
                                        },
                                        new Container
                                        {
                                            Width = 280,
                                            Height = 28,
                                            Children = new Drawable[]
                                            {
                                                new Box
                                                {
                                                    RelativeSizeAxes = Axes.Both,
                                                    Colour = new Color4(25, 25, 30, 255)
                                                },
                                                _formatTextBox = new BasicTextBox
                                                {
                                                    RelativeSizeAxes = Axes.Both,
                                                    Text = RateChanger.DefaultNameFormat,
                                                    PlaceholderText = "[[name]] [[rate]]",
                                                    CommitOnFocusLost = true
                                                }
                                            }
                                        }
                                    }
                                },
                                // Preview/count row
                                new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Direction = FillDirection.Horizontal,
                                    Spacing = new Vector2(15, 0),
                                    Children = new Drawable[]
                                    {
                                        _countText = new SpriteText
                                        {
                                            Text = "Will create: 11 maps",
                                            Font = new FontUsage("", 12),
                                            Colour = new Color4(100, 200, 255, 255)
                                        },
                                        _previewText = new SpriteText
                                        {
                                            Text = "(0.5x, 0.6x, ... 1.5x)",
                                            Font = new FontUsage("", 12),
                                            Colour = new Color4(150, 150, 150, 255)
                                        }
                                    }
                                },
                                // Apply button
                                _applyButton = new FunctionButton("Create All Rate-Changed Beatmaps")
                                {
                                    Width = 280,
                                    Height = 35,
                                    Enabled = false
                                }
                            }
                        }
                    }
                }
            }
        };

        // Wire up events
        _minRateTextBox.OnCommit += (_, _) => OnRateInputChanged();
        _maxRateTextBox.OnCommit += (_, _) => OnRateInputChanged();
        _stepTextBox.OnCommit += (_, _) => OnRateInputChanged();
        _formatTextBox.OnCommit += (_, _) => OnFormatChanged();
        _applyButton.Clicked += OnApplyClicked;

        UpdatePreview();
    }

    private Container CreateTextBox(out BasicTextBox textBox, string defaultValue, float width)
    {
        textBox = new BasicTextBox
        {
            RelativeSizeAxes = Axes.Both,
            Text = defaultValue,
            CommitOnFocusLost = true
        };

        return new Container
        {
            Width = width,
            Height = 28,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(25, 25, 30, 255)
                },
                textBox
            }
        };
    }

    private FunctionButton CreatePresetButton(string label, double min, double max, double step)
    {
        var button = new FunctionButton(label)
        {
            Width = 65,
            Height = 24
        };
        button.Clicked += () =>
        {
            _minRate = min;
            _maxRate = max;
            _step = step;
            _minRateTextBox.Text = min.ToString("0.0#", CultureInfo.InvariantCulture);
            _maxRateTextBox.Text = max.ToString("0.0#", CultureInfo.InvariantCulture);
            _stepTextBox.Text = step.ToString("0.0#", CultureInfo.InvariantCulture);
            UpdatePreview();
        };
        return button;
    }

    private void OnRateInputChanged()
    {
        // Parse min rate
        if (double.TryParse(_minRateTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var minVal))
        {
            _minRate = Math.Clamp(minVal, MIN_RATE_LIMIT, MAX_RATE_LIMIT);
        }
        _minRateTextBox.Text = _minRate.ToString("0.0#", CultureInfo.InvariantCulture);

        // Parse max rate
        if (double.TryParse(_maxRateTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var maxVal))
        {
            _maxRate = Math.Clamp(maxVal, MIN_RATE_LIMIT, MAX_RATE_LIMIT);
        }
        // Ensure max >= min
        if (_maxRate < _minRate)
            _maxRate = _minRate;
        _maxRateTextBox.Text = _maxRate.ToString("0.0#", CultureInfo.InvariantCulture);

        // Parse step
        if (double.TryParse(_stepTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var stepVal))
        {
            _step = Math.Max(stepVal, MIN_STEP);
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
    }

    private void UpdatePreview()
    {
        var rates = CalculateRates();
        _countText.Text = $"Will create: {rates.Count} maps";

        if (rates.Count <= 5)
        {
            _previewText.Text = $"({string.Join(", ", rates.Select(r => $"{r:0.0#}x"))})";
        }
        else
        {
            var first = rates.Take(2).Select(r => $"{r:0.0#}x");
            var last = rates.TakeLast(2).Select(r => $"{r:0.0#}x");
            _previewText.Text = $"({string.Join(", ", first)}, ... {string.Join(", ", last)})";
        }
    }

    private List<double> CalculateRates()
    {
        var rates = new List<double>();
        
        for (double rate = _minRate; rate < _maxRate - 0.001; rate += _step)
        {
            rates.Add(Math.Round(rate, 2));
        }
        
        // Always include max rate
        if (rates.Count == 0 || Math.Abs(rates[^1] - _maxRate) > 0.001)
        {
            rates.Add(Math.Round(_maxRate, 2));
        }

        return rates;
    }

    private void OnApplyClicked()
    {
        ApplyBulkRateClicked?.Invoke(_minRate, _maxRate, _step, _format);
    }

    public void SetEnabled(bool enabled)
    {
        _applyButton.Enabled = enabled;
    }
}
