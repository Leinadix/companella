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
using Companella.Mods;
using Companella.Services.Tools;
using osu.Framework.Extensions.Color4Extensions;

namespace Companella.Components.Tools;

/// <summary>
/// Panel for selecting and applying beatmap mods.
/// Displays mods grouped by category with automatic layout.
/// </summary>
public partial class ModSelectionPanel : CompositeDrawable
{
    [Resolved]
    private ModService ModService { get; set; } = null!;

    private FillFlowContainer _categoriesContainer = null!;
    private SpriteText _selectedModName = null!;
    private SpriteText _selectedModDescription = null!;
    private FillFlowContainer _parametersContainer = null!;
    private ModernButton _applyButton = null!;
    private SpriteText _statusText = null!;

    private IMod? _selectedMod;
    private ModButton? _selectedButton;
    private bool _enabled = false;
    private readonly List<ParameterSlider> _parameterSliders = new();

    private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);
    private readonly Color4 _categoryHeaderColor = new Color4(180, 180, 180, 255);
    private readonly Color4 _descriptionColor = new Color4(120, 120, 120, 255);

    /// <summary>
    /// Event raised when a mod should be applied.
    /// </summary>
    public event Action<IMod>? ApplyModClicked;

    /// <summary>
    /// Event raised when loading starts.
    /// </summary>
    public event Action<string>? LoadingStarted;

    /// <summary>
    /// Event raised when loading status changes.
    /// </summary>
    public event Action<string>? LoadingStatusChanged;

    /// <summary>
    /// Event raised when loading finishes.
    /// </summary>
    public event Action? LoadingFinished;

    public ModSelectionPanel()
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
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 16),
                Children = new Drawable[]
                {
                    // Header
                    new SpriteText
                    {
                        Text = "Beatmap Mods",
                        Font = new FontUsage("", 20, "Bold"),
                        Colour = Color4.White
                    },
                    // Categories container (scrollable)
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 280,
                        Masking = true,
                        CornerRadius = 6,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = new Color4(30, 30, 35, 255)
                            },
                            new BasicScrollContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                ClampExtension = 20,
                                ScrollbarVisible = true,
                                Child = _categoriesContainer = new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Direction = FillDirection.Vertical,
                                    Spacing = new Vector2(0, 12),
                                    Padding = new MarginPadding(12)
                                }
                            }
                        }
                    },
                    // Selected mod info
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Children = new Drawable[]
                        {
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical,
                                Spacing = new Vector2(0, 4),
                                Children = new Drawable[]
                                {
                                    _selectedModName = new SpriteText
                                    {
                                        Text = "No mod selected",
                                        Font = new FontUsage("", 16, "Bold"),
                                        Colour = _accentColor
                                    },
                                    _selectedModDescription = new SpriteText
                                    {
                                        Text = "Select a mod from the list above to apply it.",
                                        Font = new FontUsage("", 14),
                                        Colour = _descriptionColor
                                    }
                                }
                            }
                        }
                    },
                    // Parameter sliders container
                    _parametersContainer = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 8),
                        Padding = new MarginPadding { Left = 4, Right = 4 }
                    },
                    // Apply button and status
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(12, 0),
                        Children = new Drawable[]
                        {
                            _applyButton = new ModernButton("Apply Mod", _accentColor)
                            {
                                Size = new Vector2(120, 36),
                                Enabled = false,
                                TooltipText = "Apply the selected mod to create a new difficulty"
                            },
                            _statusText = new SpriteText
                            {
                                Text = "",
                                Font = new FontUsage("", 14),
                                Colour = _descriptionColor,
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft
                            }
                        }
                    }
                }
            }
        };

        _applyButton.Clicked += OnApplyClicked;

        // Populate mods after dependency injection
        Schedule(PopulateMods);
    }

    /// <summary>
    /// Populates the panel with registered mods grouped by category.
    /// </summary>
    private void PopulateMods()
    {
        _categoriesContainer.Clear();

        var mods = ModService.GetAllMods();
        if (mods.Count == 0)
        {
            _categoriesContainer.Add(new SpriteText
            {
                Text = "No mods registered",
                Font = new FontUsage("", 14),
                Colour = _descriptionColor
            });
            return;
        }

        // Group mods by category
        var categories = mods
            .GroupBy(m => m.Category)
            .OrderBy(g => g.Key);

        foreach (var category in categories)
        {
            // Category header
            _categoriesContainer.Add(new SpriteText
            {
                Text = category.Key,
                Font = new FontUsage("", 15, "Bold"),
                Colour = _categoryHeaderColor,
                Margin = new MarginPadding { Top = 4 }
            });

            // Mod buttons in a flow container
            var modFlow = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Full,
                Spacing = new Vector2(8, 8)
            };

            foreach (var mod in category.OrderBy(m => m.Name))
            {
                var button = new ModButton(mod, _accentColor);
                button.Selected += OnModSelected;
                modFlow.Add(button);
            }

            _categoriesContainer.Add(modFlow);
        }
    }

    /// <summary>
    /// Refreshes the mod list from the ModService.
    /// </summary>
    public void RefreshMods()
    {
        PopulateMods();
    }

    private void OnModSelected(ModButton button, IMod mod)
    {
        // Deselect previous button
        _selectedButton?.SetSelected(false);

        // Select new button
        _selectedButton = button;
        _selectedButton.SetSelected(true);
        _selectedMod = mod;

        // Update info display
        _selectedModName.Text = mod.Name;
        _selectedModDescription.Text = mod.Description;

        // Populate parameter sliders
        PopulateParameterSliders(mod);

        // Enable apply button if panel is enabled
        _applyButton.Enabled = _enabled;
    }

    /// <summary>
    /// Populates the parameter sliders for the selected mod.
    /// </summary>
    private void PopulateParameterSliders(IMod mod)
    {
        _parametersContainer.Clear();
        _parameterSliders.Clear();

        if (mod.Parameters.Count == 0)
            return;

        foreach (var param in mod.Parameters)
        {
            var slider = new ParameterSlider(param, _accentColor);
            _parameterSliders.Add(slider);
            _parametersContainer.Add(slider);
        }
    }

    private void OnApplyClicked()
    {
        if (_selectedMod == null || !_enabled)
            return;

        ApplyModClicked?.Invoke(_selectedMod);
    }

    /// <summary>
    /// Sets whether the panel is enabled for interaction.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        _applyButton.Enabled = enabled && _selectedMod != null;
    }

    /// <summary>
    /// Sets the status text displayed next to the apply button.
    /// </summary>
    public void SetStatus(string status)
    {
        _statusText.Text = status;
    }

    /// <summary>
    /// Clears the current mod selection.
    /// </summary>
    public void ClearSelection()
    {
        _selectedButton?.SetSelected(false);
        _selectedButton = null;
        _selectedMod = null;

        _selectedModName.Text = "No mod selected";
        _selectedModDescription.Text = "Select a mod from the list above to apply it.";
        _applyButton.Enabled = false;

        // Clear parameter sliders
        _parametersContainer.Clear();
        _parameterSliders.Clear();
    }

    /// <summary>
    /// Button representing a single mod.
    /// </summary>
    private partial class ModButton : CompositeDrawable
    {
        private readonly IMod _mod;
        private readonly Color4 _accentColor;
        private bool _isSelected;

        private Box _background = null!;
        private Container _border = null!;
        private SpriteText _nameText = null!;

        public event Action<ModButton, IMod>? Selected;

        public ModButton(IMod mod, Color4 accentColor)
        {
            _mod = mod;
            _accentColor = accentColor;

            Size = new Vector2(140, 36);
            Masking = true;
            CornerRadius = 4;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                _background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(45, 45, 50, 255)
                },
                _border = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 4,
                    BorderThickness = 2,
                    BorderColour = new Color4(60, 60, 65, 255),
                    Child = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Alpha = 0,
                        AlwaysPresent = true
                    }
                },
                _nameText = new SpriteText
                {
                    Text = _mod.Name + " (+" + _mod.Icon + ")",
                    Font = new FontUsage("", 23),
                    Colour = Color4.White,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                }
            };
        }

        public void SetSelected(bool selected)
        {
            _isSelected = selected;

            if (selected)
            {
                _background.FadeColour(_accentColor.Opacity(0.3f), 100);
                _border.BorderColour = _accentColor;
                _nameText.FadeColour(_accentColor, 100);
            }
            else
            {
                _background.FadeColour(new Color4(45, 45, 50, 255), 100);
                _border.BorderColour = new Color4(60, 60, 65, 255);
                _nameText.FadeColour(Color4.White, 100);
            }
        }

        protected override bool OnHover(HoverEvent e)
        {
            if (!_isSelected)
            {
                _background.FadeColour(new Color4(55, 55, 60, 255), 100);
                _border.BorderColour = new Color4(80, 80, 85, 255);
            }
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            if (!_isSelected)
            {
                _background.FadeColour(new Color4(45, 45, 50, 255), 100);
                _border.BorderColour = new Color4(60, 60, 65, 255);
            }
            base.OnHoverLost(e);
        }

        protected override bool OnClick(ClickEvent e)
        {
            Selected?.Invoke(this, _mod);
            return true;
        }
    }

    /// <summary>
    /// Modern styled button.
    /// </summary>
    private partial class ModernButton : CompositeDrawable, IHasTooltip
    {
        private readonly string _text;
        private readonly Color4 _color;
        private bool _enabled = true;

        private Box _background = null!;
        private SpriteText _label = null!;

        /// <summary>
        /// Tooltip text displayed on hover.
        /// </summary>
        public LocalisableString TooltipText { get; set; }

        public event Action? Clicked;

        public bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                if (_background != null)
                {
                    _background.FadeColour(_enabled ? _color : new Color4(60, 60, 65, 255), 100);
                    _label.FadeColour(_enabled ? Color4.White : new Color4(100, 100, 100, 255), 100);
                }
            }
        }

        public ModernButton(string text, Color4 color)
        {
            _text = text;
            _color = color;
            Masking = true;
            CornerRadius = 4;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                _background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = _enabled ? _color : new Color4(60, 60, 65, 255)
                },
                _label = new SpriteText
                {
                    Text = _text,
                    Font = new FontUsage("", 15, "Bold"),
                    Colour = _enabled ? Color4.White : new Color4(100, 100, 100, 255),
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre
                }
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            if (_enabled)
                _background.FadeColour(_color.Lighten(0.2f), 100);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            if (_enabled)
                _background.FadeColour(_color, 100);
            base.OnHoverLost(e);
        }

        protected override bool OnClick(ClickEvent e)
        {
            if (_enabled)
            {
                _background.FlashColour(Color4.White, 200, Easing.OutQuad);
                Clicked?.Invoke();
                return true;
            }
            return false;
        }
    }
}
