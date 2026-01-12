using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;
using Companella.Services;

namespace Companella.Components.Settings;

/// <summary>
/// Panel for configuring overlay position offset with +/- controls.
/// </summary>
public partial class OverlayPositionPanel : CompositeDrawable
{
    [Resolved]
    private UserSettingsService SettingsService { get; set; } = null!;

    [Resolved]
    private OsuWindowOverlayService OverlayService { get; set; } = null!;

    private SpriteText _xValueText = null!;
    private SpriteText _yValueText = null!;

    private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);
    private readonly Color4 _normalColor = new Color4(60, 60, 70, 255);
    private readonly Color4 _hoverColor = new Color4(80, 80, 90, 255);

    private const int StepSmall = 10;
    private const int StepLarge = 50;

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
                        Text = "Overlay Position Offset:",
                        Font = new FontUsage("", 16),
                        Colour = new Color4(200, 200, 200, 255)
                    },
                    new SpriteText
                    {
                        Text = "(Hold Shift for larger steps)",
                        Font = new FontUsage("", 16),
                        Colour = new Color4(150, 150, 150, 255)
                    },
                    // X offset row
                    CreateOffsetRow("X:", () => SettingsService.Settings.OverlayOffsetX, 
                        val => { SettingsService.Settings.OverlayOffsetX = val; UpdateOverlayOffset(); },
                        out _xValueText),
                    // Y offset row
                    CreateOffsetRow("Y:", () => SettingsService.Settings.OverlayOffsetY,
                        val => { SettingsService.Settings.OverlayOffsetY = val; UpdateOverlayOffset(); },
                        out _yValueText),
                    // Reset button
                    new OffsetButton
                    {
                        Width = 80,
                        Height = 24,
                        Masking = true,
                        CornerRadius = 4,
                        NormalColor = _normalColor,
                        HoverColor = _hoverColor,
                        ButtonText = "Reset",
                        Action = OnResetClicked
                    }
                }
            }
        };

        // Initialize display values
        UpdateDisplayValues();
    }

    private Container CreateOffsetRow(string label, Func<int> getValue, Action<int> setValue, out SpriteText valueText)
    {
        SpriteText capturedValueText = null!;
        
        var row = new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = 28,
            Children = new Drawable[]
            {
                new SpriteText
                {
                    Text = label,
                    Font = new FontUsage("", 15),
                    Colour = Color4.White,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft
                },
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(4, 0),
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Position = new Vector2(30, 0),
                    Children = new Drawable[]
                    {
                        new OffsetButton
                        {
                            Width = 28,
                            Height = 24,
                            Masking = true,
                            CornerRadius = 4,
                            NormalColor = _normalColor,
                            HoverColor = _hoverColor,
                            ButtonText = "-",
                            Action = () => AdjustValue(getValue, setValue, -1),
                            ShiftAction = () => AdjustValue(getValue, setValue, -1, true)
                        },
                        new Container
                        {
                            Width = 60,
                            Height = 24,
                            Masking = true,
                            CornerRadius = 4,
                            Children = new Drawable[]
                            {
                                new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Colour = new Color4(40, 40, 50, 255)
                                },
                                capturedValueText = new SpriteText
                                {
                                    Text = getValue().ToString(),
                                    Font = new FontUsage("", 15),
                                    Colour = Color4.White,
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre
                                }
                            }
                        },
                        new OffsetButton
                        {
                            Width = 28,
                            Height = 24,
                            Masking = true,
                            CornerRadius = 4,
                            NormalColor = _normalColor,
                            HoverColor = _hoverColor,
                            ButtonText = "+",
                            Action = () => AdjustValue(getValue, setValue, 1),
                            ShiftAction = () => AdjustValue(getValue, setValue, 1, true)
                        }
                    }
                }
            }
        };

        valueText = capturedValueText;
        return row;
    }

    private void AdjustValue(Func<int> getValue, Action<int> setValue, int direction, bool largeStep = false)
    {
        int step = largeStep ? StepLarge : StepSmall;
        int newValue = getValue() + (direction * step);
        
        // Clamp to reasonable range
        newValue = Math.Clamp(newValue, -2000, 2000);
        
        setValue(newValue);
        UpdateDisplayValues();
        SaveSettings();
    }

    private void OnResetClicked()
    {
        SettingsService.Settings.OverlayOffsetX = 0;
        SettingsService.Settings.OverlayOffsetY = 0;
        UpdateOverlayOffset();
        UpdateDisplayValues();
        SaveSettings();
    }

    private void UpdateDisplayValues()
    {
        if (_xValueText != null)
            _xValueText.Text = SettingsService.Settings.OverlayOffsetX.ToString();
        if (_yValueText != null)
            _yValueText.Text = SettingsService.Settings.OverlayOffsetY.ToString();
    }

    private void UpdateOverlayOffset()
    {
        if (OverlayService != null)
        {
            OverlayService.OverlayOffset = new System.Drawing.Point(
                SettingsService.Settings.OverlayOffsetX,
                SettingsService.Settings.OverlayOffsetY
            );
        }
    }

    private void SaveSettings()
    {
        Task.Run(async () => await SettingsService.SaveAsync());
    }

    /// <summary>
    /// Custom button for offset controls that supports shift-click for larger steps.
    /// </summary>
    private partial class OffsetButton : ClickableContainer
    {
        private Box _background = null!;
        private SpriteText _buttonText = null!;
        
        public Color4 NormalColor { get; set; }
        public Color4 HoverColor { get; set; }
        public string ButtonText { get; set; } = "";
        public Action? ShiftAction { get; set; }

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                _background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = NormalColor
                },
                _buttonText = new SpriteText
                {
                    Text = ButtonText,
                    Font = new FontUsage("", 15),
                    Colour = Color4.White,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre
                }
            };
        }

        protected override bool OnClick(ClickEvent e)
        {
            if (e.ShiftPressed && ShiftAction != null)
            {
                ShiftAction.Invoke();
                return true;
            }
            
            return base.OnClick(e);
        }

        protected override bool OnHover(HoverEvent e)
        {
            _background.Colour = HoverColor;
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            _background.Colour = NormalColor;
            base.OnHoverLost(e);
        }
    }
}
