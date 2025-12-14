using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;
using OsuMappingHelper.Models;

namespace OsuMappingHelper.Components;

/// <summary>
/// Panel containing function buttons.
/// </summary>
public partial class FunctionButtonPanel : CompositeDrawable
{
    private FunctionButton _analyzeBpmButton = null!;
    private FunctionButton _normalizeSvButton = null!;
    private BpmFactorToggle _bpmFactorToggle = null!;

    public event Action? AnalyzeBpmClicked;
    public event Action? NormalizeSvClicked;

    /// <summary>
    /// Gets the currently selected BPM factor.
    /// </summary>
    public BpmFactor SelectedBpmFactor => _bpmFactorToggle?.CurrentFactor ?? BpmFactor.Normal;

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(40, 40, 48, 255)
            },
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding(10),
                Child = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 6),
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Text = "Functions",
                            Font = new FontUsage("", 14, "Bold"),
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
                                _analyzeBpmButton = new FunctionButton("Analyze BPM")
                                {
                                    Width = 130,
                                    Height = 32,
                                    Enabled = false
                                },
                                _bpmFactorToggle = new BpmFactorToggle
                                {
                                    Width = 130,
                                    Height = 32
                                },
                                _normalizeSvButton = new FunctionButton("Normalize SV")
                                {
                                    Width = 130,
                                    Height = 32,
                                    Enabled = false
                                }
                            }
                        }
                    }
                }
            }
        };

        _analyzeBpmButton.Clicked += () => AnalyzeBpmClicked?.Invoke();
        _normalizeSvButton.Clicked += () => NormalizeSvClicked?.Invoke();
    }

    public void SetEnabled(bool enabled)
    {
        _analyzeBpmButton.Enabled = enabled;
        _normalizeSvButton.Enabled = enabled;
        _bpmFactorToggle.Enabled = enabled;
    }
}
