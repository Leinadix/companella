using Companella.Models.Session;
using Companella.Services.Analysis;
using Companella.Services.Beatmap;
using Companella.Services.Common;
using Companella.Services.Database;
using Companella.Services.Session;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK;
using osuTK.Graphics;
using Squirrel.SimpleSplat;

namespace Companella.Components.Session;

/// <summary>
/// Panel for planning and generating structured practice sessions.
/// Allows users to create sessions from analysis data or manual input.
/// </summary>
public partial class SessionPlannerPanel : CompositeDrawable
{
    [Resolved]
    private MapsDatabaseService MapsDatabase { get; set; } = null!;

    [Resolved]
    private OsuCollectionService CollectionService { get; set; } = null!;

    [Resolved]
    private SkillsTrendAnalyzer TrendAnalyzer { get; set; } = null!;

    private SessionPlannerService? _plannerService;
    private BeatmapIndexer? _beatmapIndexer;

    private SessionModeSelector _modeSelector = null!;
    private Container _manualInputsContainer = null!;
    private SessionSkillsetDropdown _skillsetDropdown = null!;
    private DifficultySlider _difficultySlider = null!;
    private SessionGenerateButton _generateButton = null!;
    private SpriteText _statusText = null!;
    private SpriteText _summaryText = null!;
    private FillFlowContainer _previewContainer = null!;
    private SessionPlanningSpinner _loadingSpinner = null!;

    private SkillsTrendResult? _currentTrends;
    private SessionPlan? _currentPlan;
    private bool _isGenerating;

    private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);
    private readonly Color4 _warmupColor = new Color4(100, 200, 100, 255);
    private readonly Color4 _rampUpColor = new Color4(255, 180, 100, 255);
    private readonly Color4 _cooldownColor = new Color4(100, 150, 255, 255);

    /// <summary>
    /// Event raised when a loading operation starts.
    /// </summary>
    public event Action<string>? LoadingStarted;

    /// <summary>
    /// Event raised to update the loading status.
    /// </summary>
    public event Action<string>? LoadingStatusChanged;

    /// <summary>
    /// Event raised when a loading operation finishes.
    /// </summary>
    public event Action? LoadingFinished;

    public SessionPlannerPanel()
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        _beatmapIndexer = new BeatmapIndexer();
        _plannerService = new SessionPlannerService(MapsDatabase, CollectionService, _beatmapIndexer);
        _plannerService.ProgressChanged += OnPlannerProgressChanged;

        InternalChildren = new Drawable[]
        {
            new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 10),
                Children = new Drawable[]
                {
                    // Header
                    new SpriteText
                    {
                        Text = "Session Planner",
                        Font = new FontUsage("", 17, "Bold"),
                        Colour = new Color4(180, 180, 180, 255)
                    },
                    // Description
                    new SpriteText
                    {
                        Text = "Generate a structured practice session with warmup, ramp-up, and cooldown phases.",
                        Font = new FontUsage("", 14),
                        Colour = new Color4(140, 140, 140, 255)
                    },
                    // Mode selector
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
                                Text = "Mode:",
                                Font = new FontUsage("", 15),
                                Colour = new Color4(160, 160, 160, 255),
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft
                            },
                            _modeSelector = new SessionModeSelector
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft
                            }
                        }
                    },
                    // Manual inputs container (shown when Manual mode is selected)
                    _manualInputsContainer = new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Alpha = 0,
                        Child = new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 8),
                            Children = new Drawable[]
                            {
                                // Skillset dropdown
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
                                            Text = "Focus Skillset:",
                                            Font = new FontUsage("", 15),
                                            Colour = new Color4(160, 160, 160, 255),
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft
                                        },
                                        _skillsetDropdown = new SessionSkillsetDropdown
                                        {
                                            Width = 120,
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft
                                        }
                                    }
                                },
                                // Difficulty slider
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
                                            Text = "Peak Difficulty:",
                                            Font = new FontUsage("", 15),
                                            Colour = new Color4(160, 160, 160, 255),
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft
                                        },
                                        _difficultySlider = new DifficultySlider
                                        {
                                            Width = 150,
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft
                                        }
                                    }
                                }
                            }
                        }
                    },
                    // Generate button row
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(8, 0),
                        Children = new Drawable[]
                        {
                            _generateButton = new SessionGenerateButton
                            {
                                Size = new Vector2(140, 32),
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                TooltipText = "Generate a practice session based on your skill analysis"
                            },
                            _loadingSpinner = new SessionPlanningSpinner
                            {
                                Size = new Vector2(20),
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Alpha = 0
                            }
                        }
                    },
                    // Status text
                    _statusText = new SpriteText
                    {
                        Text = "Select a mode and click Generate to create a session",
                        Font = new FontUsage("", 14),
                        Colour = new Color4(120, 120, 120, 255)
                    },
                    // Summary text
                    _summaryText = new SpriteText
                    {
                        Text = "",
                        Font = new FontUsage("", 14),
                        Colour = _accentColor,
                        Alpha = 0
                    },
                    // Preview container for session structure
                    _previewContainer = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 4),
                        Alpha = 0
                    }
                }
            }
        };

        // Wire up events
        _modeSelector.Current.BindValueChanged(OnModeChanged, true);
        _generateButton.Clicked += OnGenerateClicked;
        _difficultySlider.Current.BindValueChanged(OnDifficultyChanged);
    }

    private void OnModeChanged(ValueChangedEvent<SessionPlanMode> e)
    {
        _manualInputsContainer.FadeTo(e.NewValue == SessionPlanMode.Manual ? 1 : 0, 200);

        if (e.NewValue == SessionPlanMode.Analysis && _currentTrends == null)
        {
            _statusText.Text = "No analysis data available. Run Skills Analysis first, or use Manual mode.";
            _generateButton.SetEnabled(false);
        }
        else
        {
            _statusText.Text = "Click Generate to create a session plan";
            _generateButton.SetEnabled(true);
        }
    }

    private void OnDifficultyChanged(ValueChangedEvent<double> e)
    {
        // Update status to show selected difficulty
        if (_modeSelector.Current.Value == SessionPlanMode.Manual)
        {
            _statusText.Text = $"Peak difficulty: {e.NewValue:F1} MSD";
        }
    }

    private async void OnGenerateClicked()
    {
        if (_isGenerating || _plannerService == null)
            return;

        _isGenerating = true;
        _generateButton.SetEnabled(false);
        _loadingSpinner.FadeTo(1, 100);
        _previewContainer.FadeTo(0, 100);
        _summaryText.FadeTo(0, 100);

        LoadingStarted?.Invoke("Generating session plan...");

        try
        {
            SessionPlan? plan = null;

            if (_modeSelector.Current.Value == SessionPlanMode.Analysis)
            {
                if (_currentTrends == null)
                {
                    _statusText.Text = "No analysis data available";
                    return;
                }

                plan = await _plannerService.GenerateFromAnalysisAsync(_currentTrends);
            }
            else
            {
                var skillset = _skillsetDropdown.Current.Value;
                var peakDifficulty = _difficultySlider.Current.Value;

                plan = await _plannerService.GenerateManualAsync(
                    skillset == "Any" ? null : skillset,
                    peakDifficulty);
            }

            if (plan != null)
            {
                _currentPlan = plan;
                DisplayPlan(plan);
                _statusText.Text = $"Session created: {plan.CollectionName}";
                _summaryText.Text = plan.Summary;
                _summaryText.FadeTo(1, 200);
            }
            else
            {
                _statusText.Text = "Failed to generate session plan. Check that maps are indexed.";
            }
        }
        catch (Exception ex)
        {
            _statusText.Text = $"Error: {ex.Message}";
            Logger.Info($"[SessionPlanner] Error: {ex}");
        }
        finally
        {
            _isGenerating = false;
            _generateButton.SetEnabled(true);
            _loadingSpinner.FadeTo(0, 100);
            LoadingFinished?.Invoke();
        }
    }

    private void OnPlannerProgressChanged(object? sender, SessionPlanProgressEventArgs e)
    {
        Schedule(() =>
        {
            _statusText.Text = e.Status;
            LoadingStatusChanged?.Invoke($"{e.Percentage}%: {e.Status}");
        });
    }

    private void DisplayPlan(SessionPlan plan)
    {
        _previewContainer.Clear();

        // Add phase headers with summaries
        AddPhaseHeader("Warmup", SessionPhase.Warmup, plan, _warmupColor);
        AddPhaseHeader("Ramp-Up", SessionPhase.RampUp, plan, _rampUpColor);
        AddPhaseHeader("Cooldown", SessionPhase.Cooldown, plan, _cooldownColor);

        _previewContainer.FadeTo(1, 200);
    }

    private void AddPhaseHeader(string name, SessionPhase phase, SessionPlan plan, Color4 color)
    {
        var items = plan.GetPhaseItems(phase);
        var duration = plan.GetPhaseDurationMinutes(phase);
        var avgMsd = items.Count > 0 ? items.Average(i => i.ActualMsd) : 0;

        _previewContainer.Add(new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = 24,
            Masking = true,
            CornerRadius = 4,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(color.R, color.G, color.B, 0.15f)
                },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Padding = new MarginPadding { Horizontal = 8 },
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Text = name,
                            Font = new FontUsage("", 17, "Bold"),
                            Colour = color,
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft
                        },
                        new SpriteText
                        {
                            Text = $" - {items.Count} maps, ~{duration:F0} min, avg {avgMsd:F1} MSD",
                            Font = new FontUsage("", 14),
                            Colour = new Color4(160, 160, 160, 255),
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft
                        }
                    }
                }
            }
        });
    }

    /// <summary>
    /// Sets the current skill trends for analysis-based session generation.
    /// </summary>
    public void SetTrends(SkillsTrendResult? trends)
    {
        _currentTrends = trends;
        if (_modeSelector == null) return;

        if (trends != null && _modeSelector.Current.Value == SessionPlanMode.Analysis)
        {
            _generateButton.SetEnabled(true);
            _statusText.Text = $"Ready to generate session (skill level: {trends.OverallSkillLevel:F1})";

            // Pre-fill difficulty slider with analysis data
            _difficultySlider.Current.Value = trends.OverallSkillLevel * 1.15;
        }
        else if (_modeSelector.Current.Value == SessionPlanMode.Analysis)
        {
            _generateButton.SetEnabled(false);
            _statusText.Text = "No analysis data available. Run Skills Analysis first.";
        }
    }

    protected override void Dispose(bool isDisposing)
    {
        if (_plannerService != null)
        {
            _plannerService.ProgressChanged -= OnPlannerProgressChanged;
        }

        base.Dispose(isDisposing);
    }
}

/// <summary>
/// Radio button selector for session plan mode.
/// </summary>
public partial class SessionModeSelector : CompositeDrawable
{
    public Bindable<SessionPlanMode> Current { get; } = new Bindable<SessionPlanMode>(SessionPlanMode.Analysis);

    private SessionModeButton _analysisButton = null!;
    private SessionModeButton _manualButton = null!;

    public SessionModeSelector()
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
            Spacing = new Vector2(4, 0),
            Children = new Drawable[]
            {
                _analysisButton = new SessionModeButton("Use Analysis", SessionPlanMode.Analysis),
                _manualButton = new SessionModeButton("Manual", SessionPlanMode.Manual)
            }
        };

        _analysisButton.Clicked += () => Current.Value = SessionPlanMode.Analysis;
        _manualButton.Clicked += () => Current.Value = SessionPlanMode.Manual;

        Current.BindValueChanged(OnValueChanged, true);
    }

    private void OnValueChanged(ValueChangedEvent<SessionPlanMode> e)
    {
        _analysisButton.SetSelected(e.NewValue == SessionPlanMode.Analysis);
        _manualButton.SetSelected(e.NewValue == SessionPlanMode.Manual);
    }
}

/// <summary>
/// Individual mode selection button.
/// </summary>
public partial class SessionModeButton : CompositeDrawable
{
    private readonly string _label;
    private readonly SessionPlanMode _mode;

    private Box _background = null!;
    private Box _hoverOverlay = null!;
    private SpriteText _text = null!;
    private bool _isSelected;

    private readonly Color4 _selectedColor = new Color4(255, 102, 170, 255);
    private readonly Color4 _normalColor = new Color4(60, 60, 65, 255);

    public event Action? Clicked;

    public SessionModeButton(string label, SessionPlanMode mode)
    {
        _label = label;
        _mode = mode;
        // Size is set based on label length
        Size = new Vector2(label.Length * 8 + 20, 26);
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
                Text = _label,
                Font = new FontUsage("", 16),
                Colour = new Color4(180, 180, 180, 255),
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre
            }
        };
    }

    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        _background.FadeColour(_isSelected ? _selectedColor : _normalColor, 150);
        _text.FadeColour(_isSelected ? Color4.White : new Color4(180, 180, 180, 255), 150);
    }

    protected override bool OnHover(HoverEvent e)
    {
        if (!_isSelected)
            _hoverOverlay.FadeTo(0.1f, 100);
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        _hoverOverlay.FadeTo(0, 100);
        base.OnHoverLost(e);
    }

    protected override bool OnClick(ClickEvent e)
    {
        Clicked?.Invoke();
        return true;
    }
}

/// <summary>
/// Dropdown for selecting focus skillset.
/// </summary>
public partial class SessionSkillsetDropdown : BasicDropdown<string>
{
    public SessionSkillsetDropdown()
    {
        var skillsets = new[] { "Any", "stream", "jumpstream", "handstream", "stamina", "jackspeed", "chordjack", "technical" };
        Items = skillsets;
        Current.Value = "Any";
    }

    protected override LocalisableString GenerateItemText(string item)
    {
        return item;
    }
}

/// <summary>
/// Slider for selecting peak difficulty.
/// </summary>
public partial class DifficultySlider : CompositeDrawable
{
    public BindableDouble Current { get; } = new BindableDouble(20)
    {
        MinValue = 10,
        MaxValue = 40,
        Precision = 0.5
    };

    private BasicSliderBar<double> _slider = null!;
    private SpriteText _valueText = null!;

    public DifficultySlider()
    {
        AutoSizeAxes = Axes.Y;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChild = new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(8, 0),
            Children = new Drawable[]
            {
                _slider = new BasicSliderBar<double>
                {
                    Width = 100,
                    Height = 20,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Current = Current
                },
                _valueText = new SpriteText
                {
                    Font = new FontUsage("", 14),
                    Colour = new Color4(255, 102, 170, 255),
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft
                }
            }
        };

        Current.BindValueChanged(e => _valueText.Text = $"{e.NewValue:F1} MSD", true);
    }
}

/// <summary>
/// Button for generating a session.
/// </summary>
public partial class SessionGenerateButton : CompositeDrawable, IHasTooltip
{
    private Box _background = null!;
    private Box _hoverOverlay = null!;
    private SpriteText _label = null!;
    private bool _enabled = true;

    private readonly Color4 _enabledColor = new Color4(255, 102, 170, 255);
    private readonly Color4 _disabledColor = new Color4(80, 80, 85, 255);

    /// <summary>
    /// Tooltip text displayed on hover.
    /// </summary>
    public LocalisableString TooltipText { get; set; }

    public event Action? Clicked;

    [BackgroundDependencyLoader]
    private void load()
    {
        Masking = true;
        CornerRadius = 6;

        InternalChildren = new Drawable[]
        {
            _background = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = _enabledColor
            },
            _hoverOverlay = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.White,
                Alpha = 0
            },
            _label = new SpriteText
            {
                Text = "Generate Session",
                Font = new FontUsage("", 15, "Bold"),
                Colour = Color4.White,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre
            }
        };
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        _background.FadeColour(enabled ? _enabledColor : _disabledColor, 150);
        _label.FadeColour(enabled ? Color4.White : new Color4(120, 120, 120, 255), 150);
    }

    protected override bool OnHover(HoverEvent e)
    {
        if (_enabled)
            _hoverOverlay.FadeTo(0.15f, 100);
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        _hoverOverlay.FadeTo(0, 100);
        base.OnHoverLost(e);
    }

    protected override bool OnClick(ClickEvent e)
    {
        if (!_enabled)
            return false;

        _hoverOverlay.FadeTo(0.3f, 50).Then().FadeTo(0.15f, 100);
        Clicked?.Invoke();
        return true;
    }
}

/// <summary>
/// Loading spinner for session planning.
/// </summary>
public partial class SessionPlanningSpinner : CompositeDrawable
{
    private Box _spinner = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChild = _spinner = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Colour = new Color4(255, 102, 170, 255),
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre
        };
    }

    protected override void Update()
    {
        base.Update();
        _spinner.Rotation += (float)(Clock.ElapsedFrameTime * 0.2);
    }
}

