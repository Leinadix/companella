using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;
using Companella.Models.Beatmap;
using Companella.Components.Tools;
using Companella.Models.Difficulty;

namespace Companella.Components.Misc;

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

    public FunctionButtonPanel()
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
                        Text = "BPM Analysis",
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
                            _analyzeBpmButton = new FunctionButton("Analyze BPM")
                            {
                                Width = 100,
                                Height = 32,
                                Enabled = false,
                                TooltipText = "Detect BPM from audio and generate timing points"
                            },
                            _bpmFactorToggle = new BpmFactorToggle
                            {
                                Width = 128,
                                Height = 32
                            },
                            _normalizeSvButton = new FunctionButton("Normalize SV")
                            {
                                Width = 100,
                                Height = 32,
                                Enabled = false,
                                TooltipText = "Convert variable BPM to constant BPM with SV compensation"
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
