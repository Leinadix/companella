using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;

namespace Companella.Components.Layout;

/// <summary>
/// A visual drop zone indicator. 
/// File drops are handled at the Game level via OnFileDrop.
/// </summary>
public partial class DropZone : CompositeDrawable
{
    private Box _background = null!;
    private Container _borderContainer = null!;
    private SpriteText _label = null!;

    private readonly Color4 _normalColor = new Color4(35, 35, 40, 255);
    private readonly Color4 _hoverColor = new Color4(50, 50, 60, 255);
    private readonly Color4 _borderNormalColor = new Color4(80, 80, 90, 255);
    private readonly Color4 _borderActiveColor = new Color4(255, 102, 170, 255);

    /// <summary>
    /// Event raised when a valid .osu file is dropped.
    /// This must be triggered externally from the Game class.
    /// </summary>
    public event Action<string>? FileDropped;

    [BackgroundDependencyLoader]
    private void load()
    {
        Masking = true;
        CornerRadius = 8;

        InternalChildren = new Drawable[]
        {
            _background = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = _normalColor
            },
            _borderContainer = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 8,
                BorderThickness = 2,
                BorderColour = _borderNormalColor,
                Child = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.Transparent
                }
            },
            new FillFlowContainer
            {
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(10, 0),
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Text = "[+]",
                        Font = new FontUsage("", 19, "Bold"),
                        Colour = new Color4(100, 100, 110, 255),
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft
                    },
                    _label = new SpriteText
                    {
                        Text = "Drop .osu file here",
                        Font = new FontUsage("", 17),
                        Colour = new Color4(120, 120, 130, 255),
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft
                    }
                }
            }
        };
    }

    /// <summary>
    /// Called externally when a file is dropped on the application.
    /// </summary>
    public void HandleFileDrop(string filePath)
    {
        if (filePath.EndsWith(".osu", StringComparison.OrdinalIgnoreCase))
        {
            // Flash the border to indicate file was received
            // Note: BorderColour is ColourInfo type, can't use TransformTo with Color4
            _borderContainer.BorderColour = _borderActiveColor;
            Scheduler.AddDelayed(() => _borderContainer.BorderColour = _borderNormalColor, 250);
            _label.Text = $"Loading: {Path.GetFileName(filePath)}";

            FileDropped?.Invoke(filePath);
        }
    }

    protected override bool OnHover(HoverEvent e)
    {
        _background.FadeColour(_hoverColor, 100);
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        _background.FadeColour(_normalColor, 100);
        base.OnHoverLost(e);
    }
}
