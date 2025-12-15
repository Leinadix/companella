using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;
using OsuMappingHelper.Services;

namespace OsuMappingHelper.Components;

/// <summary>
/// Component for selecting a dan level for training.
/// Displays all 20 dan levels (1-10, alpha-kappa) as selectable buttons.
/// </summary>
public partial class DanLevelSelector : CompositeDrawable
{
    private SpriteText _titleText = null!;
    private SpriteText _selectedText = null!;
    private SpriteText _msdText = null!;
    private SpriteText _yavsrgText = null!;
    private FillFlowContainer _buttonsContainer = null!;

    private readonly List<DanButton> _buttons = new();
    private string? _selectedDan;
    private double? _currentMsd;
    private double? _currentYavsrg;

    private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);

    /// <summary>
    /// Event fired when selection changes.
    /// </summary>
    public event Action<string?>? SelectionChanged;

    /// <summary>
    /// Gets the currently selected dan label.
    /// </summary>
    public string? SelectedDan => _selectedDan;

    /// <summary>
    /// Gets whether a dan is selected.
    /// </summary>
    public bool HasSelection => !string.IsNullOrEmpty(_selectedDan);

    [BackgroundDependencyLoader]
    private void load()
    {
        Masking = true;
        CornerRadius = 6;

        InternalChildren = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(35, 33, 43, 255)
            },
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding(12),
                Children = new Drawable[]
                {
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(10, 0),
                        Children = new Drawable[]
                        {
                            _msdText = new SpriteText
                            {
                                Text = "",
                                Font = new FontUsage("", 16, "Bold"),
                                Colour = new Color4(100, 200, 255, 255),
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Alpha = 0
                            },
                            _titleText = new SpriteText
                            {
                                Text = "Assign Dan Level:",
                                Font = new FontUsage("", 16, "Bold"),
                                Colour = _accentColor,
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft
                            },
                            _yavsrgText = new SpriteText
                            {
                                Text = "",
                                Font = new FontUsage("", 16, "Bold"),
                                Colour = new Color4(255, 200, 100, 255),
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Alpha = 0
                            },
                            _selectedText = new SpriteText
                            {
                                Text = "(none selected)",
                                Font = new FontUsage("", 14),
                                Colour = new Color4(140, 140, 140, 255),
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft
                            }
                        }
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Top = 30 },
                        Child = _buttonsContainer = new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Full,
                            Spacing = new Vector2(6, 6)
                        }
                    }
                }
            }
        };

        // Create buttons for all dan levels
        var danLabels = TrainingDataService.GetAllDanLabels();
        foreach (var label in danLabels)
        {
            var button = new DanButton(label);
            button.Clicked += () => SelectDan(label);
            _buttons.Add(button);
            _buttonsContainer.Add(button);
        }
    }

    /// <summary>
    /// Selects a dan level.
    /// </summary>
    public void SelectDan(string? danLabel)
    {
        if (_selectedDan == danLabel)
        {
            // Deselect if clicking the same one
            _selectedDan = null;
        }
        else
        {
            _selectedDan = danLabel;
        }

        // Update button states
        foreach (var button in _buttons)
        {
            button.IsSelected = button.DanLabel == _selectedDan;
        }

        // Update selected text
        if (string.IsNullOrEmpty(_selectedDan))
        {
            _selectedText.Text = "(none selected)";
            _selectedText.Colour = new Color4(140, 140, 140, 255);
        }
        else
        {
            _selectedText.Text = $"Dan {_selectedDan}";
            _selectedText.Colour = _accentColor;
        }

        SelectionChanged?.Invoke(_selectedDan);
    }

    /// <summary>
    /// Clears the selection.
    /// </summary>
    public void ClearSelection()
    {
        SelectDan(null);
    }

    /// <summary>
    /// Sets the difficulty ratings to display.
    /// </summary>
    /// <param name="msd">The MSD overall difficulty rating, or null to hide.</param>
    /// <param name="yavsrg">The YAVSRG difficulty rating, or null to hide.</param>
    public void SetDifficulty(double? msd, double? yavsrg)
    {
        _currentMsd = msd;
        _currentYavsrg = yavsrg;
        UpdateDifficultyDisplay();
    }

    private void UpdateDifficultyDisplay()
    {
        // Update MSD display
        if (_currentMsd.HasValue)
        {
            _msdText.Text = $"MSD: {_currentMsd.Value:F2}";
            _msdText.FadeIn(150);
        }
        else
        {
            _msdText.FadeOut(150);
        }

        // Update YAVSRG display
        if (_currentYavsrg.HasValue)
        {
            _yavsrgText.Text = $"{_currentYavsrg.Value:F2}*";
            _yavsrgText.FadeIn(150);
        }
        else
        {
            _yavsrgText.FadeOut(150);
        }
    }

    /// <summary>
    /// A button for selecting a dan level.
    /// </summary>
    private partial class DanButton : CompositeDrawable
    {
        private Box _background = null!;
        private Box _hoverOverlay = null!;
        private SpriteText _text = null!;
        private bool _isSelected;

        private readonly Color4 _normalColor = new Color4(50, 48, 60, 255);
        private readonly Color4 _selectedColor = new Color4(255, 102, 170, 255);
        private readonly Color4 _hoverColor = new Color4(70, 68, 80, 255);

        public string DanLabel { get; }
        public event Action? Clicked;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                UpdateVisual();
            }
        }

        public DanButton(string danLabel)
        {
            DanLabel = danLabel;
            Size = new Vector2(48, 32);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Masking = true;
            CornerRadius = 4;

            // Check if it's a Greek letter (longer text)
            var isGreek = !int.TryParse(DanLabel, out _);
            var displayText = isGreek ? GetGreekAbbreviation(DanLabel) : DanLabel;

            InternalChildren = new Drawable[]
            {
                _background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = _normalColor
                },
                _hoverOverlay = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.White,
                    Alpha = 0
                },
                _text = new SpriteText
                {
                    Text = displayText,
                    Font = new FontUsage("", isGreek ? 11 : 14, "Bold"),
                    Colour = new Color4(200, 200, 200, 255),
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre
                }
            };
        }

        private static string GetGreekAbbreviation(string label)
        {
            return label.ToLower() switch
            {
                "alpha" => "a",
                "beta" => "b",
                "gamma" => "g",
                "delta" => "d",
                "epsilon" => "e",
                "zeta" => "z",
                "eta" => "n",
                "theta" => "th",
                "iota" => "i",
                "kappa" => "k",
                _ => label
            };
        }

        protected override bool OnClick(ClickEvent e)
        {
            Clicked?.Invoke();
            return true;
        }

        protected override bool OnHover(HoverEvent e)
        {
            if (!_isSelected)
            {
                _hoverOverlay.FadeTo(0.1f, 100);
                _background.FadeColour(_hoverColor, 100);
            }
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            _hoverOverlay.FadeTo(0, 100);
            if (!_isSelected)
            {
                _background.FadeColour(_normalColor, 100);
            }
            base.OnHoverLost(e);
        }

        private void UpdateVisual()
        {
            if (_isSelected)
            {
                _background.FadeColour(_selectedColor, 150);
                _text.FadeColour(Color4.White, 150);
            }
            else
            {
                _background.FadeColour(_normalColor, 150);
                _text.FadeColour(new Color4(200, 200, 200, 255), 150);
            }
        }
    }
}

