using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK;
using osuTK.Graphics;
using Companella.Models.Database;
using Companella.Models.Training;
using Companella.Services.Analysis;
using Companella.Services.Beatmap;
using Companella.Services.Database;
using Companella.Services.Platform;
using Companella.Services.Session;
using Companella.Components.Charts;
using Companella.Models.Session;
using Companella.Services.Tools;
using Companella.Services.Common;
using Companella.Components.Misc;

namespace Companella.Components.Analysis;

/// <summary>
/// Panel displaying map recommendations based on player skill and focus mode.
/// </summary>
public partial class MapRecommendationPanel : CompositeDrawable
{
    [Resolved]
    private MapsDatabaseService MapsDatabase { get; set; } = null!;
    
    [Resolved]
    private MapMmrCalculator MmrCalculator { get; set; } = null!;
    
    [Resolved]
    private SkillsTrendAnalyzer TrendAnalyzer { get; set; } = null!;
    
    [Resolved]
    private MapRecommendationService RecommendationService { get; set; } = null!;
    
    [Resolved]
    private OsuProcessDetector ProcessDetector { get; set; } = null!;
    
    [Resolved]
    private OsuCollectionService CollectionService { get; set; } = null!;
    
    [Resolved]
    private OsuFileParser FileParser { get; set; } = null!;
    
    private RateChanger _rateChanger = null!;

    private FocusDropdown _focusDropdown = null!;
    private SkillsetDropdown _skillsetDropdown = null!;
    private Container _skillsetContainer = null!;
    private RecommendationRefreshButton _refreshButton = null!;
    private QuickRestartButton _restartButton = null!;
    private FillFlowContainer _recommendationsContainer = null!;
    private SpriteText _statusText = null!;
    private SpriteText _summaryText = null!;
    private RecommendationLoadingSpinner _loadingSpinner = null!;

    private RecommendationBatch? _currentBatch;
    private SkillsTrendResult? _currentTrends;
    private OsuRestartDialog? _restartDialog;

    /// <summary>
    /// Event raised when a map is selected for loading.
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

    private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);

    public MapRecommendationPanel()
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        _rateChanger = new RateChanger();
        
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
                    // Header
                    new SpriteText
                    {
                        Text = "Map Recommendations",
                        Font = new FontUsage("", 17, "Bold"),
                        Colour = new Color4(180, 180, 180, 255)
                    },
                    // Controls - row 1: Focus and Skillset
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
                                Text = "Focus:",
                                Font = new FontUsage("", 15),
                                Colour = new Color4(160, 160, 160, 255),
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft
                            },
                            _focusDropdown = new FocusDropdown
                            {
                                Width = 110,
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft
                            },
                            _skillsetContainer = new Container
                            {
                                AutoSizeAxes = Axes.Both,
                                Alpha = 0,
                                Child = new FillFlowContainer
                                {
                                    AutoSizeAxes = Axes.Both,
                                    Direction = FillDirection.Horizontal,
                                    Spacing = new Vector2(6, 0),
                                    Children = new Drawable[]
                                    {
                                        new SpriteText
                                        {
                                            Text = "Skillset:",
                                            Font = new FontUsage("", 15),
                                            Colour = new Color4(160, 160, 160, 255),
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft
                                        },
                                        _skillsetDropdown = new SkillsetDropdown
                                        {
                                            Width = 100,
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft
                                        }
                                    }
                                }
                            }
                        }
                    },
                    // Controls - row 2: Buttons
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(8, 0),
                        Children = new Drawable[]
                        {
                            _refreshButton = new RecommendationRefreshButton
                            {
                                Size = new Vector2(80, 24),
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft
                            },
                            _restartButton = new QuickRestartButton
                            {
                                Size = new Vector2(90, 24),
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft
                            },
                            _loadingSpinner = new RecommendationLoadingSpinner
                            {
                                Size = new Vector2(18),
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Alpha = 0
                            }
                        }
                    },
                    // Status/summary row
                    _statusText = new SpriteText
                    {
                        Text = "Select focus mode and click Refresh",
                        Font = new FontUsage("", 14),
                        Colour = new Color4(120, 120, 120, 255)
                    },
                    _summaryText = new SpriteText
                    {
                        Text = "",
                        Font = new FontUsage("", 14),
                        Colour = _accentColor,
                        Alpha = 0
                    },
                    // Recommendations list
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Child = _recommendationsContainer = new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 4)
                        }
                    }
                }
            }
        };

        // Setup dropdowns
        _focusDropdown.Items = Enum.GetValues<RecommendationFocus>();
        _focusDropdown.Current.Value = RecommendationFocus.Push;
        _focusDropdown.Current.BindValueChanged(OnFocusChanged);

        _skillsetDropdown.Items = MapRecommendationService.GetAvailableSkillsets();
        _skillsetDropdown.Current.Value = "stream";

        _refreshButton.Clicked += OnRefreshClicked;
        _restartButton.Clicked += OnRestartClicked;
    }

    /// <summary>
    /// Sets the current skill trends for recommendations.
    /// </summary>
    public void SetTrends(SkillsTrendResult? trends)
    {
        _currentTrends = trends;
        
        if (trends == null || trends.TotalPlays == 0)
        {
            _statusText.Text = "Need session data to generate recommendations";
            _summaryText.Alpha = 0;
            _recommendationsContainer.Clear();
        }
    }

    private void OnFocusChanged(ValueChangedEvent<RecommendationFocus> e)
    {
        // Show skillset dropdown only for Skillset focus
        _skillsetContainer.FadeTo(e.NewValue == RecommendationFocus.Skillset ? 1 : 0, 200);
    }

    private void OnRefreshClicked()
    {
        _ = GenerateRecommendationsAsync();
    }

    private void OnRestartClicked()
    {
        // Show restart dialog with command line args options
        ShowRestartDialog(
            "Restart osu!",
            ProcessDetector.IsOsuRunning
                ? "osu! is currently running. It will be force closed and restarted with the selected arguments."
                : "osu! will be started with the selected arguments."
        );
    }

    private void PerformRestart(string arguments)
    {
        _statusText.Text = "Restarting osu!...";
        Task.Run(() =>
        {
            CollectionService.RestartOsu(arguments);
            Schedule(() => _statusText.Text = string.IsNullOrEmpty(arguments)
                ? "osu! restart initiated"
                : $"osu! restart initiated with: {arguments}");
        });
    }

    private void ShowRestartDialog(string title, string message)
    {
        if (_restartDialog == null)
        {
            _restartDialog = new OsuRestartDialog();
            AddInternal(_restartDialog);
        }

        // Remove old handler and add new one
        _restartDialog.Confirmed -= OnRestartConfirmed;
        _restartDialog.Confirmed += OnRestartConfirmed;
        _restartDialog.Show(title, message);
    }

    private void OnRestartConfirmed(string arguments)
    {
        PerformRestart(arguments);
    }

    private async Task GenerateRecommendationsAsync()
    {
        if (_currentTrends == null || _currentTrends.TotalPlays == 0)
        {
            _statusText.Text = "Need session data first";
            return;
        }

        var mapCount = MapsDatabase.Get4KMapCount();
        if (mapCount == 0)
        {
            _statusText.Text = "No maps indexed. Use Settings tab to index maps.";
            return;
        }

        _loadingSpinner.FadeTo(1, 100);
        _refreshButton.Enabled.Value = false;
        _statusText.Text = "Generating recommendations...";
        _recommendationsContainer.Clear();

        try
        {
            var focus = _focusDropdown.Current.Value;
            var skillset = focus == RecommendationFocus.Skillset ? _skillsetDropdown.Current.Value : null;

            await Task.Run(() =>
            {
                _currentBatch = RecommendationService.GetRecommendations(focus, _currentTrends, 8, skillset);
            });

            Schedule(() => DisplayRecommendations(_currentBatch));
        }
        catch (Exception ex)
        {
            Schedule(() =>
            {
                _statusText.Text = $"Error: {ex.Message}";
            });
        }
        finally
        {
            Schedule(() =>
            {
                _loadingSpinner.FadeTo(0, 100);
                _refreshButton.Enabled.Value = true;
            });
        }
    }

    private void DisplayRecommendations(RecommendationBatch? batch)
    {
        _recommendationsContainer.Clear();

        if (batch == null || batch.Recommendations.Count == 0)
        {
            _statusText.Text = "No suitable maps found. Try indexing more maps.";
            _summaryText.Alpha = 0;
            return;
        }

        _summaryText.Text = batch.Summary;
        _summaryText.FadeTo(1, 200);

        foreach (var rec in batch.Recommendations)
        {
            _recommendationsContainer.Add(new RecommendationCard(rec, _accentColor)
            {
                MapSelected = () => MapSelected?.Invoke(rec)
            });
        }

        // Update the osu! collection with recommended maps
        UpdateOsuCollection(batch.Recommendations);
    }

    /// <summary>
    /// Updates the Companella! collection in osu! with the recommended maps.
    /// Creates rate-changed beatmaps when needed.
    /// </summary>
    private void UpdateOsuCollection(List<MapRecommendation> recommendations)
    {
        // Check if any maps need rate changing
        var mapsNeedingRateChange = recommendations.Where(r => 
            !string.IsNullOrEmpty(r.BeatmapPath) && r.NeedsRateChange && r.SuggestedRate.HasValue).ToList();

        if (mapsNeedingRateChange.Count == 0)
        {
            // No rate changes needed, just update collection directly
            var paths = recommendations
                .Where(r => !string.IsNullOrEmpty(r.BeatmapPath))
                .Select(r => r.BeatmapPath)
                .ToList();
            
            if (paths.Count > 0)
            {
                var success = CollectionService.UpdateCollection(paths);
                _statusText.Text = success 
                    ? $"Added {paths.Count} maps to 'Companella!' collection" 
                    : $"Found {recommendations.Count} recommendations";
            }
            return;
        }

        // Need to create rate-changed maps - use loading overlay
        _ = CreateRateChangedMapsAndUpdateCollectionAsync(recommendations);
    }

    private async Task CreateRateChangedMapsAndUpdateCollectionAsync(List<MapRecommendation> recommendations)
    {
        Schedule(() => LoadingStarted?.Invoke("Checking ffmpeg..."));

        try
        {
            // Check ffmpeg availability
            var ffmpegAvailable = await _rateChanger.CheckFfmpegAvailableAsync();
            if (!ffmpegAvailable)
            {
                Schedule(() =>
                {
                    LoadingFinished?.Invoke();
                    _statusText.Text = "ffmpeg not found! Install ffmpeg to create rate-changed maps.";
                });
                
                // Fall back to original maps only
                var originalPaths = recommendations
                    .Where(r => !string.IsNullOrEmpty(r.BeatmapPath))
                    .Select(r => r.BeatmapPath)
                    .ToList();
                    
                if (originalPaths.Count > 0)
                {
                    CollectionService.UpdateCollection(originalPaths);
                }
                return;
            }

            var beatmapPaths = new List<string>();
            var rateChangedCount = 0;
            var totalToProcess = recommendations.Count(r => !string.IsNullOrEmpty(r.BeatmapPath));
            var processed = 0;

            foreach (var rec in recommendations.Where(r => !string.IsNullOrEmpty(r.BeatmapPath)))
            {
                processed++;
                
                if (rec.NeedsRateChange && rec.SuggestedRate.HasValue)
                {
                    // Create rate-changed beatmap
                    try
                    {
                        var statusMsg = $"Creating {rec.SuggestedRate:0.0#}x ({processed}/{totalToProcess}): {rec.DisplayName}";
                        Schedule(() => LoadingStatusChanged?.Invoke(statusMsg));
                        
                        var osuFile = FileParser.Parse(rec.BeatmapPath);
                        var newPath = await _rateChanger.CreateRateChangedBeatmapAsync(
                            osuFile,
                            rec.SuggestedRate.Value,
                            RateChanger.DefaultNameFormat,
                            pitchAdjust: true,
                            progressCallback: status => Schedule(() => LoadingStatusChanged?.Invoke(status))
                        );
                        
                        beatmapPaths.Add(newPath);
                        rateChangedCount++;
                        Logger.Info($"[Recommendations] Created rate-changed map: {Path.GetFileName(newPath)}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Info($"[Recommendations] Failed to create rate change for {rec.DisplayName}: {ex.Message}");
                        // Fall back to original beatmap
                        beatmapPaths.Add(rec.BeatmapPath);
                    }
                }
                else
                {
                    // Use original beatmap at 1.0x
                    beatmapPaths.Add(rec.BeatmapPath);
                }
            }

            // Update collection with all paths
            Schedule(() => LoadingStatusChanged?.Invoke("Updating collection..."));
            
            if (beatmapPaths.Count > 0)
            {
                Logger.Info($"[Recommendations] Updating collection with {beatmapPaths.Count} maps ({rateChangedCount} rate-changed):");
                foreach (var path in beatmapPaths)
                {
                    Logger.Info($"  - {Path.GetFileName(path)} (exists: {File.Exists(path)})");
                }
                
                var success = CollectionService.UpdateCollection(beatmapPaths);
                
                Schedule(() =>
                {
                    LoadingFinished?.Invoke();
                    if (success)
                    {
                        if (rateChangedCount > 0)
                        {
                            // Note: rate-changed maps need osu! restart to be indexed
                            _statusText.Text = $"Added {beatmapPaths.Count} maps ({rateChangedCount} rate-changed) - Restart osu! to see new maps";
                        }
                        else
                        {
                            _statusText.Text = $"Added {beatmapPaths.Count} maps to 'Companella!' collection";
                        }
                    }
                    else
                    {
                        _statusText.Text = $"Found {recommendations.Count} recommendations (collection update failed)";
                    }
                });
            }
            else
            {
                Schedule(() =>
                {
                    LoadingFinished?.Invoke();
                    _statusText.Text = $"Found {recommendations.Count} recommendations";
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Info($"[Recommendations] Error: {ex.Message}");
            Schedule(() =>
            {
                LoadingFinished?.Invoke();
                _statusText.Text = $"Error creating rate-changed maps: {ex.Message}";
            });
        }
    }
}

/// <summary>
/// Card displaying a single map recommendation.
/// </summary>
public partial class RecommendationCard : CompositeDrawable
{
    private readonly MapRecommendation _recommendation;
    private readonly Color4 _accentColor;
    
    private Box _background = null!;
    private Box _hoverOverlay = null!;

    public Action? MapSelected;

    public RecommendationCard(MapRecommendation recommendation, Color4 accentColor)
    {
        _recommendation = recommendation;
        _accentColor = accentColor;

        RelativeSizeAxes = Axes.X;
        Height = 50;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Masking = true;
        CornerRadius = 4;

        var skillsetColor = SkillsOverTimeChart.SkillsetColors.GetValueOrDefault(
            _recommendation.Map?.DominantSkillset.ToLowerInvariant() ?? "unknown",
            SkillsOverTimeChart.SkillsetColors["unknown"]
        );

        InternalChildren = new Drawable[]
        {
            _background = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(40, 40, 45, 255)
            },
            _hoverOverlay = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.White,
                Alpha = 0
            },
            // Skillset indicator bar
            new Box
            {
                Width = 4,
                RelativeSizeAxes = Axes.Y,
                Colour = skillsetColor
            },
            new FillFlowContainer
            {
                RelativeSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Padding = new MarginPadding { Left = 10, Right = 8, Vertical = 6 },
                Spacing = new Vector2(0, 2),
                Children = new Drawable[]
                {
                    // Title row
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(6, 0),
                        Children = new Drawable[]
                        {
                            new SpriteText
                            {
                                Text = TruncateText(_recommendation.DisplayName, 45),
                                Font = new FontUsage("", 15),
                                Colour = Color4.White
                            },
                            new SpriteText
                            {
                                Text = _recommendation.NeedsRateChange 
                                    ? $"@ {_recommendation.SuggestedRate:0.0#}x" 
                                    : "",
                                Font = new FontUsage("", 17, "Bold"),
                                Colour = _accentColor
                            }
                        }
                    },
                    // Stats row
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(10, 0),
                        Children = new Drawable[]
                        {
                            new SpriteText
                            {
                                Text = $"{_recommendation.Map?.OverallMsd:F1} MSD",
                                Font = new FontUsage("", 13),
                                Colour = skillsetColor
                            },
                            new SpriteText
                            {
                                Text = _recommendation.Map?.DominantSkillset ?? "",
                                Font = new FontUsage("", 13),
                                Colour = new Color4(150, 150, 150, 255)
                            },
                            new SpriteText
                            {
                                Text = $"Rel: {_recommendation.RelativeDifficulty:F2}",
                                Font = new FontUsage("", 13),
                                Colour = new Color4(120, 120, 120, 255)
                            }
                        }
                    },
                    // Reasoning row
                    new SpriteText
                    {
                        Text = TruncateText(_recommendation.Reasoning, 60),
                        Font = new FontUsage("", 13),
                        Colour = new Color4(100, 100, 100, 255)
                    }
                }
            }
        };
    }

    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
    }

    protected override bool OnHover(HoverEvent e)
    {
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
        _hoverOverlay.FadeTo(0.2f, 50).Then().FadeTo(0.1f, 100);
        MapSelected?.Invoke();
        return true;
    }
}

/// <summary>
/// Dropdown for selecting recommendation focus.
/// </summary>
public partial class FocusDropdown : BasicDropdown<RecommendationFocus>
{
    protected override LocalisableString GenerateItemText(RecommendationFocus item)
    {
        return item switch
        {
            RecommendationFocus.Skillset => "Skillset",
            RecommendationFocus.Consistency => "Consistency",
            RecommendationFocus.Push => "Push",
            RecommendationFocus.DeficitFixing => "Fix Deficit",
            _ => item.ToString()
        };
    }
}

/// <summary>
/// Dropdown for selecting skillset.
/// </summary>
public partial class SkillsetDropdown : BasicDropdown<string>
{
    protected override LocalisableString GenerateItemText(string item)
    {
        return item;
    }
}

/// <summary>
/// Refresh button for recommendations.
/// </summary>
public partial class RecommendationRefreshButton : CompositeDrawable
{
    private Box _background = null!;
    private Box _hoverOverlay = null!;

    public readonly BindableBool Enabled = new BindableBool(true);
    public event Action? Clicked;

    private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);

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
                Colour = _accentColor
            },
            _hoverOverlay = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.White,
                Alpha = 0
            },
            new SpriteText
            {
                Text = "Find Maps",
                Font = new FontUsage("", 17, "Bold"),
                Colour = Color4.White,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre
            }
        };

        Enabled.BindValueChanged(e =>
        {
            this.FadeTo(e.NewValue ? 1 : 0.5f, 100);
        }, true);
    }

    protected override bool OnHover(HoverEvent e)
    {
        if (Enabled.Value)
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
        if (!Enabled.Value) return false;

        _hoverOverlay.FadeTo(0.3f, 50).Then().FadeTo(0.15f, 100);
        Clicked?.Invoke();
        return true;
    }
}

/// <summary>
/// Simple loading spinner for recommendations.
/// </summary>
public partial class RecommendationLoadingSpinner : CompositeDrawable
{
    private Box _spinner = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChild = _spinner = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Colour = new Color4(255, 102, 170, 255),
            Origin = Anchor.Centre,
            Anchor = Anchor.Centre
        };
    }

    protected override void Update()
    {
        base.Update();
        
        if (Alpha > 0)
        {
            _spinner.Rotation += (float)(Time.Elapsed * 0.3);
        }
    }
}

/// <summary>
/// Button for quickly restarting osu! to reload collections.
/// </summary>
public partial class QuickRestartButton : CompositeDrawable
{
    private Box _background = null!;
    private Box _hoverOverlay = null!;

    public readonly BindableBool Enabled = new BindableBool(true);
    public event Action? Clicked;

    private readonly Color4 _buttonColor = new Color4(80, 160, 220, 255);

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
                Colour = _buttonColor
            },
            _hoverOverlay = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.White,
                Alpha = 0
            },
            new SpriteText
            {
                Text = "Restart osu!",
                Font = new FontUsage("", 17, "Bold"),
                Colour = Color4.White,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre
            }
        };

        Enabled.BindValueChanged(e =>
        {
            this.FadeTo(e.NewValue ? 1 : 0.5f, 100);
        }, true);
    }

    protected override bool OnHover(HoverEvent e)
    {
        if (Enabled.Value)
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
        if (!Enabled.Value) return false;

        _hoverOverlay.FadeTo(0.3f, 50).Then().FadeTo(0.15f, 100);
        Clicked?.Invoke();
        return true;
    }
}

