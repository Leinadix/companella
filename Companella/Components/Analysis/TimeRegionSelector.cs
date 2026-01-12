using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK;
using osuTK.Graphics;
using Companella.Services.Database;
using Companella.Services.Analysis;

namespace Companella.Components.Analysis;

/// <summary>
/// Selector for choosing time regions for trend analysis.
/// Displays preset buttons: Last Week, Last Month, Last 3 Months, All Time.
/// </summary>
public partial class TimeRegionSelector : CompositeDrawable
{
    /// <summary>
    /// The currently selected time region.
    /// </summary>
    public Bindable<TimeRegion> Current { get; } = new Bindable<TimeRegion>(TimeRegion.LastMonth);

    private FillFlowContainer _buttonsContainer = null!;
    private readonly Dictionary<TimeRegion, TimeRegionButton> _buttons = new();

    private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);

    public TimeRegionSelector()
    {
        AutoSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChild = new FillFlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(6, 0),
            Children = new Drawable[]
            {
                new SpriteText
                {
                    Text = "Period:",
                    Font = new FontUsage("", 17),
                    Colour = new Color4(140, 140, 140, 255),
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft
                },
                _buttonsContainer = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(4, 0)
                }
            }
        };

        // Create buttons for each time region
        foreach (TimeRegion region in Enum.GetValues<TimeRegion>())
        {
            var button = new TimeRegionButton(region, GetRegionLabel(region), _accentColor)
            {
                TooltipText = GetRegionTooltip(region)
            };
            button.Clicked += () => OnButtonClicked(region);
            _buttons[region] = button;
            _buttonsContainer.Add(button);
        }

        // Subscribe to value changes
        Current.BindValueChanged(OnValueChanged, true);
    }

    private void OnValueChanged(ValueChangedEvent<TimeRegion> e)
    {
        // Update button states
        foreach (var (region, button) in _buttons)
        {
            button.SetSelected(region == e.NewValue);
        }
    }

    private void OnButtonClicked(TimeRegion region)
    {
        Current.Value = region;
    }

    private static string GetRegionLabel(TimeRegion region)
    {
        return region switch
        {
            TimeRegion.LastWeek => "Week",
            TimeRegion.LastMonth => "Month",
            TimeRegion.Last3Months => "3 Mo",
            TimeRegion.AllTime => "All",
            _ => region.ToString()
        };
    }

    private static string GetRegionTooltip(TimeRegion region)
    {
        return region switch
        {
            TimeRegion.LastWeek => "Analyze plays from the last 7 days",
            TimeRegion.LastMonth => "Analyze plays from the last 30 days",
            TimeRegion.Last3Months => "Analyze plays from the last 3 months",
            TimeRegion.AllTime => "Analyze all recorded plays",
            _ => ""
        };
    }
}

/// <summary>
/// Button for selecting a time region.
/// </summary>
public partial class TimeRegionButton : CompositeDrawable, IHasTooltip
{
    private readonly TimeRegion _region;
    private readonly string _label;
    private readonly Color4 _accentColor;
    
    private Box _background = null!;
    private Box _hoverOverlay = null!;
    private SpriteText _text = null!;
    private bool _isSelected;

    /// <summary>
    /// Tooltip text displayed on hover.
    /// </summary>
    public LocalisableString TooltipText { get; set; }

    /// <summary>
    /// Event raised when the button is clicked.
    /// </summary>
    public event Action? Clicked;

    public TimeRegionButton(TimeRegion region, string label, Color4 accentColor)
    {
        _region = region;
        _label = label;
        _accentColor = accentColor;
        
        AutoSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Masking = true;
        CornerRadius = 4;

        InternalChildren = new Drawable[]
        {
            _background = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(50, 50, 55, 255)
            },
            _hoverOverlay = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.White,
                Alpha = 0
            },
            new Container
            {
                AutoSizeAxes = Axes.Both,
                Padding = new MarginPadding { Horizontal = 10, Vertical = 4 },
                Child = _text = new SpriteText
                {
                    Text = _label,
                    Font = new FontUsage("", 14),
                    Colour = new Color4(180, 180, 180, 255)
                }
            }
        };
    }

    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        
        if (selected)
        {
            _background.FadeColour(_accentColor, 150);
            _text.FadeColour(Color4.White, 150);
        }
        else
        {
            _background.FadeColour(new Color4(50, 50, 55, 255), 150);
            _text.FadeColour(new Color4(180, 180, 180, 255), 150);
        }
    }

    protected override bool OnHover(HoverEvent e)
    {
        if (!_isSelected)
        {
            _hoverOverlay.FadeTo(0.1f, 100);
        }
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        _hoverOverlay.FadeTo(0, 100);
        base.OnHoverLost(e);
    }

    protected override bool OnClick(ClickEvent e)
    {
        _hoverOverlay.FadeTo(0.2f, 50).Then().FadeTo(_isSelected ? 0 : 0.1f, 100);
        Clicked?.Invoke();
        return true;
    }
}
