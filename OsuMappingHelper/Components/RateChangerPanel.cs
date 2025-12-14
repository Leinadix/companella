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
using TextBox = osu.Framework.Graphics.UserInterface.TextBox;

namespace OsuMappingHelper.Components;

/// <summary>
/// Panel for changing beatmap playback rate.
/// </summary>
public partial class RateChangerPanel : CompositeDrawable
{
    private BasicTextBox _rateTextBox = null!;
    private BasicTextBox _formatTextBox = null!;
    private FunctionButton _applyButton = null!;
    private FunctionButton _previewButton = null!;
    private SpriteText _previewText = null!;

    public event Action<double, string>? ApplyRateClicked;

    private double _currentRate = 1.0;
    private string _currentFormat = RateChanger.DefaultNameFormat;

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
                                    Text = "Rate Changer",
                                    Font = new FontUsage("", 16, "Bold"),
                                    Colour = new Color4(255, 102, 170, 255)
                                },
                                // Rate input row
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
                                            Text = "Rate:",
                                            Font = new FontUsage("", 14),
                                            Colour = new Color4(200, 200, 200, 255),
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft
                                        },
                                        CreateRateButton("0.5x", 0.5),
                                        CreateRateButton("0.75x", 0.75),
                                        CreateRateButton("0.9x", 0.9),
                                        new Container
                                        {
                                            Width = 70,
                                            Height = 30,
                                            Children = new Drawable[]
                                            {
                                                new Box
                                                {
                                                    RelativeSizeAxes = Axes.Both,
                                                    Colour = new Color4(25, 25, 30, 255)
                                                },
                                                _rateTextBox = new BasicTextBox
                                                {
                                                    RelativeSizeAxes = Axes.Both,
                                                    Text = "1.0",
                                                    PlaceholderText = "Rate",
                                                    CommitOnFocusLost = true
                                                }
                                            }
                                        },
                                        new SpriteText
                                        {
                                            Text = "x",
                                            Font = new FontUsage("", 14),
                                            Colour = new Color4(150, 150, 150, 255),
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft
                                        },
                                        CreateRateButton("1.1x", 1.1),
                                        CreateRateButton("1.2x", 1.2),
                                        CreateRateButton("1.3x", 1.3),
                                        CreateRateButton("1.5x", 1.5)
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
                                            Width = 300,
                                            Height = 30,
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
                                        },
                                        _previewButton = new FunctionButton("Preview")
                                        {
                                            Width = 70,
                                            Height = 30
                                        }
                                    }
                                },
                                // Format help text
                                new SpriteText
                                {
                                    Text = "Tags: [[name]] [[rate]] [[bpm]] [[od]] [[hp]] [[cs]] [[ar]]",
                                    Font = new FontUsage("", 11),
                                    Colour = new Color4(120, 120, 120, 255)
                                },
                                // Preview row
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
                                            Font = new FontUsage("", 12),
                                            Colour = new Color4(150, 150, 150, 255),
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft
                                        },
                                        _previewText = new SpriteText
                                        {
                                            Text = "...",
                                            Font = new FontUsage("", 12),
                                            Colour = new Color4(100, 200, 255, 255),
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft
                                        }
                                    }
                                },
                                // Apply button
                                _applyButton = new FunctionButton("Create Rate-Changed Beatmap")
                                {
                                    Width = 250,
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
        _rateTextBox.OnCommit += OnRateTextCommit;
        _formatTextBox.OnCommit += OnFormatTextCommit;
        _previewButton.Clicked += UpdatePreview;
        _applyButton.Clicked += OnApplyClicked;
    }

    private FunctionButton CreateRateButton(string label, double rate)
    {
        var button = new FunctionButton(label)
        {
            Width = 45,
            Height = 30
        };
        button.Clicked += () => SetRate(rate);
        return button;
    }

    private void SetRate(double rate)
    {
        _currentRate = rate;
        _rateTextBox.Text = rate.ToString("0.0#", CultureInfo.InvariantCulture);
        UpdatePreview();
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
        UpdatePreview();
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
    }

    private void UpdatePreview()
    {
        PreviewRequested?.Invoke(_currentRate, _currentFormat);
    }

    private void OnApplyClicked()
    {
        ApplyRateClicked?.Invoke(_currentRate, _currentFormat);
    }

    /// <summary>
    /// Event to request a preview update from the main screen.
    /// </summary>
    public event Action<double, string>? PreviewRequested;

    /// <summary>
    /// Updates the preview text.
    /// </summary>
    public void SetPreviewText(string text)
    {
        _previewText.Text = text;
    }

    public void SetEnabled(bool enabled)
    {
        _applyButton.Enabled = enabled;
    }
}
