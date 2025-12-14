using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osuTK;
using osuTK.Graphics;
using TextBox = osu.Framework.Graphics.UserInterface.TextBox;

namespace OsuMappingHelper.Components;

/// <summary>
/// Panel for inputting and applying universal offset changes.
/// </summary>
public partial class OffsetInputPanel : CompositeDrawable
{
    private BasicTextBox _offsetTextBox = null!;
    private FunctionButton _applyButton = null!;
    private FunctionButton _plusButton = null!;
    private FunctionButton _minusButton = null!;

    public event Action<double>? ApplyOffsetClicked;

    private double _currentOffset = 0;

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
                            Spacing = new Vector2(0, 10),
                            Children = new Drawable[]
                            {
                                new SpriteText
                                {
                                    Text = "Universal Offset",
                                    Font = new FontUsage("", 16, "Bold"),
                                    Colour = new Color4(255, 102, 170, 255)
                                },
                                new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Direction = FillDirection.Horizontal,
                                    Spacing = new Vector2(8, 0),
                                    Children = new Drawable[]
                                    {
                                        _minusButton = new FunctionButton("-10")
                                        {
                                            Width = 50,
                                            Height = 35
                                        },
                                        new Container
                                        {
                                            Width = 120,
                                            Height = 35,
                                            Children = new Drawable[]
                                            {
                                                new Box
                                                {
                                                    RelativeSizeAxes = Axes.Both,
                                                    Colour = new Color4(25, 25, 30, 255)
                                                },
                                                _offsetTextBox = new BasicTextBox
                                                {
                                                    RelativeSizeAxes = Axes.Both,
                                                    Text = "0",
                                                    PlaceholderText = "Offset (ms)",
                                                    CommitOnFocusLost = true
                                                }
                                            }
                                        },
                                        new SpriteText
                                        {
                                            Text = "ms",
                                            Font = new FontUsage("", 14),
                                            Colour = new Color4(150, 150, 150, 255),
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft
                                        },
                                        _plusButton = new FunctionButton("+10")
                                        {
                                            Width = 50,
                                            Height = 35
                                        },
                                        _applyButton = new FunctionButton("Apply Offset")
                                        {
                                            Width = 120,
                                            Height = 35,
                                            Enabled = false
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        // Wire up events
        _offsetTextBox.OnCommit += OnTextCommit;
        _plusButton.Clicked += () => AdjustOffset(10);
        _minusButton.Clicked += () => AdjustOffset(-10);
        _applyButton.Clicked += OnApplyClicked;
    }

    private void OnTextCommit(TextBox sender, bool newText)
    {
        if (double.TryParse(sender.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            _currentOffset = value;
        }
        else
        {
            // Reset to current offset if invalid
            sender.Text = _currentOffset.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    private void AdjustOffset(double delta)
    {
        _currentOffset += delta;
        _offsetTextBox.Text = _currentOffset.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private void OnApplyClicked()
    {
        if (double.TryParse(_offsetTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var offset))
        {
            ApplyOffsetClicked?.Invoke(offset);
        }
    }

    public void SetEnabled(bool enabled)
    {
        _applyButton.Enabled = enabled;
        _plusButton.Enabled = enabled;
        _minusButton.Enabled = enabled;
    }

    /// <summary>
    /// Resets the offset input to zero.
    /// </summary>
    public void Reset()
    {
        _currentOffset = 0;
        _offsetTextBox.Text = "0";
    }
}
