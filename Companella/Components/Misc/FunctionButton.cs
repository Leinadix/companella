using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK.Graphics;

namespace Companella.Components.Misc;

/// <summary>
/// A styled button for function actions.
/// </summary>
public partial class FunctionButton : CompositeDrawable, IHasTooltip
{
    private readonly string _text;
    private Box _background = null!;
    private Box _hoverOverlay = null!;
    private bool _isEnabled = true;

    /// <summary>
    /// Tooltip text displayed on hover.
    /// </summary>
    public LocalisableString TooltipText { get; set; }

    private readonly Color4 _normalColor = new Color4(255, 102, 170, 255);
    private readonly Color4 _hoverColor = new Color4(255, 130, 190, 255);
    private readonly Color4 _disabledColor = new Color4(100, 100, 100, 255);

    public event Action? Clicked;

    public FunctionButton(string text)
    {
        _text = text;
    }

    public bool Enabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            // Only update colour if already loaded
            if (_background != null)
                _background.Colour = _isEnabled ? _normalColor : _disabledColor;
        }
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Masking = true;
        CornerRadius = 5;

        InternalChildren = new Drawable[]
        {
            _background = new Box
            {
                RelativeSizeAxes = Axes.Both,
                // Use current enabled state for initial colour
                Colour = _isEnabled ? _normalColor : _disabledColor
            },
            _hoverOverlay = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.White,
                Alpha = 0
            },
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Child = new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = _text,
                    Font = new FontUsage("", 19, "Bold"),
                    Colour = Color4.White
                }
            }
        };
    }

    protected override bool OnHover(HoverEvent e)
    {
        if (_isEnabled)
        {
            _hoverOverlay.FadeTo(0.1f, 100);
            _background.FadeColour(_hoverColor, 100);
        }
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        _hoverOverlay.FadeTo(0, 100);
        _background.FadeColour(_isEnabled ? _normalColor : _disabledColor, 100);
        base.OnHoverLost(e);
    }

    protected override bool OnClick(ClickEvent e)
    {
        if (_isEnabled)
        {
            Clicked?.Invoke();
            
            // Click animation
            _hoverOverlay.FadeTo(0.3f, 50).Then().FadeTo(0.1f, 100);
        }
        return true;
    }
}
