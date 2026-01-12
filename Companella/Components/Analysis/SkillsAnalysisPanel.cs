using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;
using Companella.Models.Session;
using Companella.Services.Analysis;
using Companella.Services.Database;
using Companella.Components.Charts;
using Companella.Models.Training;
using Companella.Services.Common;

namespace Companella.Components.Analysis;

/// <summary>
/// Panel for analyzing skill trends over time and getting map recommendations.
/// Combines: TimeRegionSelector, SkillsOverTimeChart, skill level stats, and MapRecommendationPanel.
/// </summary>
public partial class SkillsAnalysisPanel : CompositeDrawable
{
    [Resolved]
    private SessionDatabaseService DatabaseService { get; set; } = null!;
    
    [Resolved]
    private SkillsTrendAnalyzer TrendAnalyzer { get; set; } = null!;

    private TimeRegionSelector _timeRegionSelector = null!;
    private SkillsOverTimeChart _skillsChart = null!;
    private FillFlowContainer _statsContainer = null!;
    private Container _skillLevelsContainer = null!;
    private MapRecommendationPanel _recommendationPanel = null!;
    private SpriteText _statusText = null!;
    private SpriteText _trendSummaryText = null!;

    private SkillsTrendResult? _currentTrends;

    private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);

    /// <summary>
    /// Event raised when a map recommendation is selected.
    /// </summary>
    public event Action<MapRecommendation>? MapSelected;

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

    /// <summary>
    /// Event raised when trends are updated (for session planner integration).
    /// </summary>
    public event Action<SkillsTrendResult?>? TrendsUpdated;

    public SkillsAnalysisPanel()
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
                Spacing = new Vector2(0, 12),
                Padding = new MarginPadding { Bottom = 40 }, // Extra space at bottom for scrolling
                Children = new Drawable[]
                {
                    // Header
                    new SpriteText
                    {
                        Text = "Skills Analysis",
                        Font = new FontUsage("", 17, "Bold"),
                        Colour = new Color4(180, 180, 180, 255)
                    },
                    // Time region selector
                    _timeRegionSelector = new TimeRegionSelector(),
                    // Status text
                    _statusText = new SpriteText
                    {
                        Text = "Select a time period to analyze",
                        Font = new FontUsage("", 14),
                        Colour = new Color4(120, 120, 120, 255)
                    },
                    // Trend summary
                    _trendSummaryText = new SpriteText
                    {
                        Text = "",
                        Font = new FontUsage("", 14),
                        Colour = _accentColor,
                        Alpha = 0
                    },
                    // Stats container
                    _statsContainer = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 6),
                        Alpha = 0,
                        Children = new Drawable[]
                        {
                            // Skill levels
                            _skillLevelsContainer = new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y
                            }
                        }
                    },
                    // Skills over time chart
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 220,
                        Masking = true,
                        CornerRadius = 6,
                        Child = _skillsChart = new SkillsOverTimeChart
                        {
                            RelativeSizeAxes = Axes.Both
                        }
                    },
                    // Map recommendations panel
                    _recommendationPanel = new MapRecommendationPanel(),
                }
            }
        };

        _timeRegionSelector.Current.BindValueChanged(OnTimeRegionChanged, true);
        _recommendationPanel.MapSelected += rec => MapSelected?.Invoke(rec);
        
        // Forward loading events from recommendation panel
        _recommendationPanel.LoadingStarted += status => LoadingStarted?.Invoke(status);
        _recommendationPanel.LoadingStatusChanged += status => LoadingStatusChanged?.Invoke(status);
        _recommendationPanel.LoadingFinished += () => LoadingFinished?.Invoke();
    }

    private void OnTimeRegionChanged(ValueChangedEvent<TimeRegion> e)
    {
        AnalyzeTrends(e.NewValue);
    }

    /// <summary>
    /// Analyzes trends for the selected time region.
    /// </summary>
    private void AnalyzeTrends(TimeRegion region)
    {
        _statusText.Text = "Analyzing trends...";

        try
        {
            _currentTrends = TrendAnalyzer.AnalyzeTrends(region);

            if (_currentTrends.TotalPlays == 0)
            {
                _statusText.Text = $"No plays found for selected period";
                _trendSummaryText.Alpha = 0;
                _statsContainer.FadeTo(0, 200);
                _skillsChart.Clear();
                _recommendationPanel.SetTrends(null);
                TrendsUpdated?.Invoke(null);
                return;
            }

            // Update status
            var regionName = GetRegionDisplayName(region);
            _statusText.Text = $"Analyzed {_currentTrends.TotalPlays} plays ({regionName})";

            // Update trend summary
            UpdateTrendSummary();

            // Update skill levels display
            UpdateSkillLevelsDisplay();

            // Update chart
            _skillsChart.SetTrendData(_currentTrends);

            // Update recommendations panel
            _recommendationPanel.SetTrends(_currentTrends);

            // Notify listeners (for session planner integration)
            TrendsUpdated?.Invoke(_currentTrends);

            _statsContainer.FadeTo(1, 200);
            _trendSummaryText.FadeTo(1, 200);
        }
        catch (Exception ex)
        {
            _statusText.Text = $"Error: {ex.Message}";
            Logger.Info($"[SkillsAnalysis] Error analyzing trends: {ex.Message}");
        }
    }

    private void UpdateTrendSummary()
    {
        if (_currentTrends == null) return;

        var overallTrend = _currentTrends.TrendSlopes.GetValueOrDefault("overall", 0);
        var trendDirection = overallTrend > 0.3 ? "improving" 
            : overallTrend < -0.3 ? "declining" 
            : "stable";

        var strongest = _currentTrends.GetStrongestSkillsets(1).FirstOrDefault() ?? "N/A";
        var weakest = _currentTrends.GetWeakestSkillsets(1).FirstOrDefault() ?? "N/A";

        _trendSummaryText.Text = $"Trend: {trendDirection} | Best: {strongest} ({_currentTrends.CurrentSkillLevels.GetValueOrDefault(strongest, 0):F1}) | Weakest: {weakest} ({_currentTrends.CurrentSkillLevels.GetValueOrDefault(weakest, 0):F1})";
    }

    private void UpdateSkillLevelsDisplay()
    {
        _skillLevelsContainer.Clear();

        if (_currentTrends == null || _currentTrends.CurrentSkillLevels.Count == 0)
            return;

        var flowContainer = new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(6, 4),
            Children = new Drawable[]
            {
                new SpriteText
                {
                    Text = "Levels:",
                    Font = new FontUsage("", 14),
                    Colour = new Color4(140, 140, 140, 255),
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft
                }
            }
        };

        // Sort by skill level descending
        var sortedSkills = _currentTrends.CurrentSkillLevels
            .Where(kvp => kvp.Key != "overall" && kvp.Key != "unknown" && kvp.Value > 0)
            .OrderByDescending(kvp => kvp.Value)
            .ToList();

        foreach (var (skillset, level) in sortedSkills)
        {
            var color = SkillsOverTimeChart.SkillsetColors.GetValueOrDefault(
                skillset.ToLowerInvariant(),
                SkillsOverTimeChart.SkillsetColors["unknown"]);

            var trend = _currentTrends.TrendSlopes.GetValueOrDefault(skillset, 0);
            var trendIndicator = trend > 0.3 ? "+" : trend < -0.3 ? "-" : "";

            flowContainer.Add(new SkillLevelBadge(skillset, level, trend, color));
        }

        _skillLevelsContainer.Add(flowContainer);
    }

    private static string GetRegionDisplayName(TimeRegion region)
    {
        return region switch
        {
            TimeRegion.LastWeek => "last 7 days",
            TimeRegion.LastMonth => "last 30 days",
            TimeRegion.Last3Months => "last 3 months",
            TimeRegion.AllTime => "all time",
            _ => region.ToString()
        };
    }

    /// <summary>
    /// Refreshes the analysis with current data.
    /// </summary>
    public void Refresh()
    {
        AnalyzeTrends(_timeRegionSelector.Current.Value);
    }
}

/// <summary>
/// Badge displaying a skill level with trend indicator.
/// </summary>
public partial class SkillLevelBadge : CompositeDrawable
{
    public SkillLevelBadge(string skillset, double level, double trend, Color4 color)
    {
        AutoSizeAxes = Axes.Both;

        var trendIndicator = trend > 0.3 ? "^" : trend < -0.3 ? "v" : "";
        var trendColor = trend > 0.3 ? new Color4(100, 255, 100, 255) 
            : trend < -0.3 ? new Color4(255, 100, 100, 255) 
            : new Color4(180, 180, 180, 255);

        InternalChild = new Container
        {
            AutoSizeAxes = Axes.Both,
            Masking = true,
            CornerRadius = 4,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(color.R, color.G, color.B, 0.2f)
                },
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Padding = new MarginPadding { Horizontal = 6, Vertical = 2 },
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Text = $"{skillset}: ",
                            Font = new FontUsage("", 13),
                            Colour = color
                        },
                        new SpriteText
                        {
                            Text = $"{level:F1}",
                            Font = new FontUsage("", 13, "Bold"),
                            Colour = color
                        },
                        new SpriteText
                        {
                            Text = trendIndicator,
                            Font = new FontUsage("", 13, "Bold"),
                            Colour = trendColor,
                            Margin = new MarginPadding { Left = 2 }
                        }
                    }
                }
            }
        };
    }
}

