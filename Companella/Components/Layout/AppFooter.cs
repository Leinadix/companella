using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using Companella.Services.Common;
using Companella.Components.Misc;
using osuTK.Graphics;

namespace Companella.Components.Layout;

/// <summary>
/// Application footer with credit and donation links.
/// </summary>
public partial class AppFooter : CompositeDrawable
{
    [Resolved]
    private SquirrelUpdaterService AutoUpdaterService { get; set; } = null!;

    private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);
    private readonly Color4 _kofiColor = new Color4(255, 95, 95, 255);

    [BackgroundDependencyLoader]
    private void load()
    {
        RelativeSizeAxes = Axes.X;
        Height = 28;
        Anchor = Anchor.BottomLeft;
        Origin = Anchor.BottomLeft;

        InternalChildren = new Drawable[]
        {
            // Semi-transparent footer background
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(15, 13, 20, 220)
            },
            // Accent line at top
            new Box
            {
                RelativeSizeAxes = Axes.X,
                Height = 1,
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Colour = _accentColor,
                Alpha = 0.5f
            },
            // Content container
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Left = 15, Right = 15 },
                Children = new Drawable[]
                {
                    // Version display (left)
                    new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = AutoUpdaterService.CurrentVersion,
                        Font = new FontUsage("", 14),
                        Colour = new Color4(120, 120, 120, 255)
                    },
                    // Copyright / credit link (center)
                    new LinkText(
                        "Companella! -Leyna",
                        "https://github.com/Leinadix/companella",
                        fontSize: 13f,
                        normalColor: new Color4(160, 160, 160, 255),
                        hoverColor: _accentColor)
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre
                    },
                    // Ko-fi link (right)
                    CreateKofiLink()
                }
            }
        };
    }

    private Drawable CreateKofiLink()
    {
        return new Container
        {
            AutoSizeAxes = Axes.Both,
            Anchor = Anchor.CentreRight,
            Origin = Anchor.CentreRight,
            Masking = true,
            CornerRadius = 4,
            Children = new Drawable[]
            {
                // Ko-fi styled background
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = _kofiColor
                },
                new Container
                {
                    AutoSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Left = 8, Right = 8, Top = 3, Bottom = 3 },
                    Child = new LinkText(
                        "buymeacoffee",
                        "https://buymeacoffee.com/leynadev",
                        fontSize: 13f,
                        fontWeight: "Bold",
                        normalColor: Color4.White,
                        hoverColor: new Color4(255, 230, 230, 255))
                }
            }
        };
    }
}
