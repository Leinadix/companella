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
using Companella.Components.Misc;

namespace Companella.Components.Tools;

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

    public OffsetInputPanel()
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
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 8),
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Text = "Universal Offset",
                        Font = new FontUsage("", 15, "Bold"),
                        Colour = new Color4(180, 180, 180, 255)
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
                                Width = 45,
                                Height = 32,
                                TooltipText = "Decrease offset by 10ms"
                            },
                            new Container
                            {
                                Width = 90,
                                Height = 32,
                                Masking = true,
                                CornerRadius = 4,
                                Children = new Drawable[]
                                {
                                    new Box
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Colour = new Color4(35, 35, 40, 255)
                                    },
                                    _offsetTextBox = new BasicTextBox
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Text = "0",
                                        PlaceholderText = "ms",
                                        CommitOnFocusLost = true
                                    }
                                }
                            },
                            new Container
                            {
                                Width = 25,
                                Height = 32,
                                Child = new SpriteText
                                {
                                    Text = "ms",
                                    Font = new FontUsage("", 16),
                                    Colour = new Color4(100, 100, 100, 255),
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft
                                }
                            },
                            _plusButton = new FunctionButton("+10")
                            {
                                Width = 45,
                                Height = 32,
                                TooltipText = "Increase offset by 10ms"
                            },
                            _applyButton = new FunctionButton("Apply")
                            {
                                Width = 70,
                                Height = 32,
                                Enabled = false,
                                TooltipText = "Shift all timing points by the specified milliseconds"
                            }
                        }
                    }
                }
            }
        };

        // Wire up events - use value change for immediate feedback
        _offsetTextBox.Current.BindValueChanged(_ => OnTextChanged());
        _plusButton.Clicked += () => AdjustOffset(10);
        _minusButton.Clicked += () => AdjustOffset(-10);
        _applyButton.Clicked += OnApplyClicked;
    }

    private void OnTextChanged()
    {
        if (double.TryParse(_offsetTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            _currentOffset = value;
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
