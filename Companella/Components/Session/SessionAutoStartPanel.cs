using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK;
using osuTK.Graphics;
using Companella.Services;

namespace Companella.Components.Session;

/// <summary>
/// Panel for configuring auto-start/end session settings.
/// </summary>
public partial class SessionAutoStartPanel : CompositeDrawable
{
    [Resolved]
    private UserSettingsService SettingsService { get; set; } = null!;

    private SettingsCheckbox _autoStartCheckbox = null!;
    private SettingsCheckbox _autoEndCheckbox = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;

        InternalChildren = new Drawable[]
        {
            new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 8),
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Text = "Session Tracking:",
                        Font = new FontUsage("", 16),
                        Colour = new Color4(200, 200, 200, 255)
                    },
                    _autoStartCheckbox = new SettingsCheckbox
                    {
                        LabelText = "Auto-start session on startup",
                        IsChecked = SettingsService.Settings.AutoStartSession,
                        TooltipText = "Automatically start tracking when the app launches"
                    },
                    _autoEndCheckbox = new SettingsCheckbox
                    {
                        LabelText = "Auto-end session on exit",
                        IsChecked = SettingsService.Settings.AutoEndSession,
                        TooltipText = "Automatically end and save the session when closing the app"
                    }
                }
            }
        };

        _autoStartCheckbox.CheckedChanged += OnAutoStartChanged;
        _autoEndCheckbox.CheckedChanged += OnAutoEndChanged;
    }

    private void OnAutoStartChanged(bool isChecked)
    {
        SettingsService.Settings.AutoStartSession = isChecked;
        SaveSettings();
    }

    private void OnAutoEndChanged(bool isChecked)
    {
        SettingsService.Settings.AutoEndSession = isChecked;
        SaveSettings();
    }

    private void SaveSettings()
    {
        Task.Run(async () => await SettingsService.SaveAsync());
    }
}

/// <summary>
/// Simple checkbox control for settings.
/// </summary>
public partial class SettingsCheckbox : CompositeDrawable, IHasTooltip
{
    private Box _checkboxBackground = null!;
    private Box _checkmark = null!;
    private SpriteText _label = null!;
    private bool _isChecked;

    public string LabelText { get; set; } = "Option";
    
    /// <summary>
    /// Tooltip text displayed on hover.
    /// </summary>
    public LocalisableString TooltipText { get; set; }
    
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            UpdateVisual();
        }
    }

    public event Action<bool>? CheckedChanged;

    private readonly Color4 _uncheckedColor = new Color4(60, 60, 70, 255);
    private readonly Color4 _checkedColor = new Color4(255, 102, 170, 255);
    private readonly Color4 _hoverColor = new Color4(80, 80, 90, 255);

    [BackgroundDependencyLoader]
    private void load()
    {
        AutoSizeAxes = Axes.Both;

        InternalChildren = new Drawable[]
        {
            new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(8, 0),
                Children = new Drawable[]
                {
                    new Container
                    {
                        Size = new Vector2(18),
                        Masking = true,
                        CornerRadius = 3,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Children = new Drawable[]
                        {
                            _checkboxBackground = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = _uncheckedColor
                            },
                            _checkmark = new Box
                            {
                                Size = new Vector2(10),
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Colour = Color4.White,
                                Alpha = 0
                            }
                        }
                    },
                    _label = new SpriteText
                    {
                        Text = LabelText,
                        Font = new FontUsage("", 15),
                        Colour = new Color4(180, 180, 180, 255),
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft
                    }
                }
            }
        };

        UpdateVisual();
    }

    private void UpdateVisual()
    {
        if (_checkboxBackground == null) return;
        
        _checkboxBackground.FadeColour(_isChecked ? _checkedColor : _uncheckedColor, 100);
        _checkmark.FadeTo(_isChecked ? 1 : 0, 100);
    }

    protected override bool OnHover(HoverEvent e)
    {
        if (!_isChecked)
            _checkboxBackground.FadeColour(_hoverColor, 100);
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        _checkboxBackground.FadeColour(_isChecked ? _checkedColor : _uncheckedColor, 100);
        base.OnHoverLost(e);
    }

    protected override bool OnClick(ClickEvent e)
    {
        _isChecked = !_isChecked;
        UpdateVisual();
        CheckedChanged?.Invoke(_isChecked);
        return true;
    }
}

