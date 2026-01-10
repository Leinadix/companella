using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;
using OsuMappingHelper.Services;

namespace OsuMappingHelper.Components;

/// <summary>
/// Settings panel for adjusting the global UI scale.
/// </summary>
public partial class UIScalePanel : CompositeDrawable
{
    [Resolved]
    private ScaledContentContainer ScaledContainer { get; set; } = null!;

    [Resolved]
    private UserSettingsService UserSettingsService { get; set; } = null!;

    private StyledSliderBar _scaleSlider = null!;
    private SpriteText _scaleValue = null!;

    private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);
    private readonly Color4 _backgroundColor = new Color4(40, 40, 45, 255);

    // Bindable for the slider
    private readonly BindableNumber<float> _scaleBindable = new BindableFloat(1.0f)
    {
        MinValue = 0.5f,
        MaxValue = 2.0f,
        Precision = 0.01f
    };

    [BackgroundDependencyLoader]
    private void load()
    {
        AutoSizeAxes = Axes.Y;
        Masking = true;
        CornerRadius = 8;

        InternalChildren = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = _backgroundColor
            },
            new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Padding = new MarginPadding(12),
                Spacing = new Vector2(0, 10),
                Children = new Drawable[]
                {
                    // Header
                    new SpriteText
                    {
                        Text = "UI Scale",
                        Font = new FontUsage("", 19, "Bold"),
                        Colour = _accentColor
                    },
                    // Description
                    new SpriteText
                    {
                        Text = "Adjust the size of all UI elements",
                        Font = new FontUsage("", 15),
                        Colour = new Color4(160, 160, 160, 255)
                    },
                    // Slider row
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(10, 0),
                        Children = new Drawable[]
                        {
                            // Min label
                            new SpriteText
                            {
                                Text = "100%",
                                Font = new FontUsage("", 14),
                                Colour = new Color4(120, 120, 120, 255),
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft
                            },
                            // Slider container
                            new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 24,
                                Width = 0.7f,
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Children = new Drawable[]
                                {
                                    // Track background
                                    new Container
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        Height = 4,
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Masking = true,
                                        CornerRadius = 2,
                                        Child = new Box
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            Colour = new Color4(60, 60, 65, 255)
                                        }
                                    },
                                    _scaleSlider = new StyledSliderBar
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        Height = 24,
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Current = _scaleBindable
                                    }
                                }
                            },
                            // Max label
                            new SpriteText
                            {
                                Text = "200%",
                                Font = new FontUsage("", 14),
                                Colour = new Color4(120, 120, 120, 255),
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft
                            }
                        }
                    },
                    // Current value display
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(8, 0),
                        Children = new Drawable[]
                        {
                            new SpriteText
                            {
                                Text = "Current:",
                                Font = new FontUsage("", 16),
                                Colour = new Color4(180, 180, 180, 255)
                            },
                            _scaleValue = new SpriteText
                            {
                                Text = "100%",
                                Font = new FontUsage("", 19, "Bold"),
                                Colour = Color4.White
                            }
                        }
                    },
                    // Preset buttons
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(8, 0),
                        Children = new Drawable[]
                        {
                            CreatePresetButton("50%", 0.5f),
                            CreatePresetButton("75%", 0.75f),
                            CreatePresetButton("100%", 1.0f),
                            CreatePresetButton("125%", 1.25f),
                            CreatePresetButton("150%", 1.5f),
                            CreatePresetButton("175%", 1.75f),
                            CreatePresetButton("200%", 2.0f)
                        }
                    }
                }
            }
        };

        // Initialize with current value
        _scaleBindable.Value = ScaledContainer.UIScale;

        // Bind slider changes
        _scaleBindable.BindValueChanged(e =>
        {
            ScaledContainer.UIScale = e.NewValue;
            UpdateScaleDisplay(e.NewValue);
            SaveSettings(e.NewValue);
        });

        // Initialize display
        UpdateScaleDisplay(ScaledContainer.UIScale);
    }

    private Drawable CreatePresetButton(string label, float scale)
    {
        return new PresetScaleButton(label, scale, _accentColor)
        {
            Action = () =>
            {
                _scaleBindable.Value = scale;
            }
        };
    }

    private void UpdateScaleDisplay(float scale)
    {
        _scaleValue.Text = $"{scale:P0}";
    }

    private void SaveSettings(float scale)
    {
        if (UserSettingsService == null) return;

        UserSettingsService.Settings.UIScale = scale;
        Task.Run(async () => await UserSettingsService.SaveAsync());
    }

    /// <summary>
    /// Styled slider bar for the UI scale.
    /// </summary>
    private partial class StyledSliderBar : BasicSliderBar<float>
    {
        private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);

        [BackgroundDependencyLoader]
        private void load()
        {
            BackgroundColour = Color4.Transparent;
            SelectionColour = _accentColor;
            KeyboardStep = 0.01f;
        }
    }

    /// <summary>
    /// Preset scale button.
    /// </summary>
    private partial class PresetScaleButton : CompositeDrawable
    {
        public Action? Action { get; set; }

        private readonly string _label;
        private readonly float _scale;
        private readonly Color4 _accentColor;

        private Box _background = null!;
        private SpriteText _text = null!;

        private readonly Color4 _normalBg = new Color4(50, 50, 55, 255);
        private readonly Color4 _hoverBg = new Color4(70, 70, 75, 255);

        public PresetScaleButton(string label, float scale, Color4 accentColor)
        {
            _label = label;
            _scale = scale;
            _accentColor = accentColor;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Size = new Vector2(60, 28);
            Masking = true;
            CornerRadius = 4;

            InternalChildren = new Drawable[]
            {
                _background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = _normalBg
                },
                _text = new SpriteText
                {
                    Text = _label,
                    Font = new FontUsage("", 15),
                    Colour = Color4.White,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre
                }
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            _background.FadeColour(_hoverBg, 100);
            _text.FadeColour(_accentColor, 100);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            _background.FadeColour(_normalBg, 100);
            _text.FadeColour(Color4.White, 100);
            base.OnHoverLost(e);
        }

        protected override bool OnClick(ClickEvent e)
        {
            Action?.Invoke();
            return true;
        }
    }
}
