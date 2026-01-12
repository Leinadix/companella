using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;
using Companella.Components.Analysis;
using Companella.Components.Layout;
using Companella.Components.Misc;
using Companella.Components.Session;
using Companella.Components.Settings;
using Companella.Components.Tools;
using Companella.Models.Application;
using Companella.Models.Beatmap;
using Companella.Models.Difficulty;
using Companella.Services.Analysis;
using Companella.Services.Beatmap;
using Companella.Services.Common;
using Companella.Services.Database;
using Companella.Services.Platform;
using Companella.Services.Session;
using Companella.Services.Tools;
using Companella.Models.Training;
using Companella.Mods;

namespace Companella.Screens;

/// <summary>
/// Main screen of the Companella! application.
/// </summary>
public partial class MainScreen : osu.Framework.Screens.Screen
{
    [Resolved]
    private OsuProcessDetector ProcessDetector { get; set; } = null!;

    [Resolved]
    private OsuFileParser FileParser { get; set; } = null!;

    [Resolved]
    private OsuWindowOverlayService OverlayService { get; set; } = null!;

    [Resolved]
    private ModService ModService { get; set; } = null!;

    [Resolved]
    private UserSettingsService UserSettingsService { get; set; } = null!;

    [Resolved]
    private SquirrelUpdaterService AutoUpdaterService { get; set; } = null!;

    [Resolved]
    private AptabaseService AptabaseService { get; set; } = null!;

    // Header components
    private MapInfoDisplay _mapInfoDisplay = null!;
    
    // Tab container
    private TabContainer _tabContainer = null!;
    
    // Gameplay tab components
    private RateChangerPanel _rateChangerPanel = null!;
    private ModSelectionPanel _modSelectionPanel = null!;
    private SessionTrackerPanel _sessionTrackerPanel = null!;
    private SessionHistoryPanel _sessionHistoryPanel = null!;
    private SkillsAnalysisPanel _skillsAnalysisPanel = null!;
    private SessionPlannerPanel _sessionPlannerPanel = null!;
    
    // Mapping tab components
    private FunctionButtonPanel _functionPanel = null!;
    private OffsetInputPanel _offsetPanel = null!;
    private BulkRateChangerPanel _bulkRateChangerPanel = null!;
    private MarathonCreatorPanel _marathonCreatorPanel = null!;

    private AppFooter _appFooter = null!;
    private LoadingOverlay _loadingOverlay = null!;
    
    // Update dialog
    private UpdateDialog _updateDialog = null!;
    
    // Window decoration
    private CustomTitleBar _titleBar = null!;
    
    // Background box for transparency control
    private Box _backgroundBox = null!;

    private OsuFile? _currentOsuFile;
    private string? _lastDetectedBeatmap;
    private double _beatmapCheckTimer;
    private const double BeatmapCheckInterval = 1000; // Check every 1 second
    
    // Store BPM factor for use in background task
    private BpmFactor _pendingBpmFactor;

    [BackgroundDependencyLoader]
    private void load()
    {
        // Create tab contents
        var gameplayTabContent = CreateGameplayTab();
        var mappingTabContent = CreateMappingTab();
        var settingsTabContent = CreateSettingsTab();

        InternalChildren = new Drawable[]
        {
            // Dark background (transparent in overlay mode) - must be first (behind everything)
            _backgroundBox = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(25, 25, 30, 255)
            },
            // Main layout using GridContainer for proportional sizing
            new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Left = 15, Right = 15, Top = 47, Bottom = 43 }, // Top padding for title bar, bottom padding for footer
                RowDimensions = new[]
                {
                    new Dimension(GridSizeMode.Relative, 0.4f),  // Map info header (2.5x bigger)
                    new Dimension(GridSizeMode.Relative, 0.6f),   // Tab container (takes remaining space)
                },
                Content = new[]
                {
                    new Drawable[]
                    {
                        _mapInfoDisplay = new MapInfoDisplay
                        {
                            RelativeSizeAxes = Axes.Both
                        }
                    },
                    new Drawable[]
                    {
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Padding = new MarginPadding { Top = 10, Bottom = 10 },
                            Child = _tabContainer = new TabContainer(
                                new[] { "Gameplay", "Mapping", "Settings" },
                                new[] { gameplayTabContent, mappingTabContent, settingsTabContent })
                            {
                                RelativeSizeAxes = Axes.Both
                            }
                        }
                    }
                }
            },
            // App footer (bottom of screen)
            _appFooter = new AppFooter(),
            // Custom osu!-styled title bar (on top of content, visible in non-overlay mode)
            _titleBar = new CustomTitleBar
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Alpha = 1f // Visible by default, will be hidden in overlay mode
            },
            // Loading overlay (on top of everything)
            _loadingOverlay = new LoadingOverlay(),
            // Update dialog (topmost)
            _updateDialog = new UpdateDialog()
        };

        // Wire up events
        //_dropZone.FileDropped += OnFileDropped;
        _functionPanel.AnalyzeBpmClicked += OnAnalyzeBpmClicked;
        _functionPanel.NormalizeSvClicked += OnNormalizeSvClicked;
        _offsetPanel.ApplyOffsetClicked += OnApplyOffsetClicked;
        _rateChangerPanel.ApplyRateClicked += OnApplyRateClicked;
        _rateChangerPanel.PreviewRequested += OnRatePreviewRequested;
        _rateChangerPanel.FormatChanged += OnRateChangerFormatChanged;
        _rateChangerPanel.PitchAdjustChanged += OnRateChangerPitchAdjustChanged;
        _bulkRateChangerPanel.ApplyBulkRateClicked += OnApplyBulkRateClicked;
        _bulkRateChangerPanel.FormatChanged += OnRateChangerFormatChanged;
        _bulkRateChangerPanel.PitchAdjustChanged += OnBulkRateChangerPitchAdjustChanged;
        _tabContainer.TabChanged += OnTabChanged;

        // Restore saved rate changer format to both panels
        var savedFormat = UserSettingsService.Settings.RateChangerFormat;
        if (!string.IsNullOrWhiteSpace(savedFormat))
        {
            _rateChangerPanel.SetFormat(savedFormat);
            _bulkRateChangerPanel.SetFormat(savedFormat);
        }

        // Restore saved pitch adjust setting to both panels
        var savedPitchAdjust = UserSettingsService.Settings.RateChangerPitchAdjust;
        _rateChangerPanel.PitchAdjust = savedPitchAdjust;
        _bulkRateChangerPanel.PitchAdjust = savedPitchAdjust;

        // Try to attach to osu! process
        TryAttachToOsu();

        // Check for updates in background
        CheckForUpdatesAsync();
    }

    private async void CheckForUpdatesAsync()
    {
        try
        {
            // Small delay to let the UI settle
            await Task.Delay(2000);

            var updateInfo = await AutoUpdaterService.CheckForUpdatesAsync();
            
            // Track analytics
            AptabaseService.TrackUpdateCheck(updateInfo != null);
            
            if (updateInfo != null)
            {
                Schedule(() =>
                {
                    _updateDialog.Show(updateInfo, AutoUpdaterService);
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Info($"[MainScreen] Update check failed: {ex.Message}");
        }
    }

    private Container CreateGameplayTab()
    {
        // Create panels for gameplay features (auto-sized)
        _rateChangerPanel = new RateChangerPanel();
        _modSelectionPanel = new ModSelectionPanel();
        _sessionTrackerPanel = new SessionTrackerPanel();
        _sessionHistoryPanel = new SessionHistoryPanel();
        _skillsAnalysisPanel = new SkillsAnalysisPanel();
        _sessionPlannerPanel = new SessionPlannerPanel();
        
        // Wire up mod selection panel
        _modSelectionPanel.ApplyModClicked += OnApplyModClicked;
        _modSelectionPanel.LoadingStarted += status =>
        {
            Schedule(() => _loadingOverlay.Show(status));
        };
        _modSelectionPanel.LoadingStatusChanged += status =>
        {
            Schedule(() => _loadingOverlay.UpdateStatus(status));
        };
        _modSelectionPanel.LoadingFinished += () =>
        {
            Schedule(() => _loadingOverlay.Hide());
        };
        
        // Wire up skills analysis panel to load recommended maps
        _skillsAnalysisPanel.MapSelected += OnRecommendedMapSelected;
        
        // Wire up loading overlay events from recommendation panel
        _skillsAnalysisPanel.LoadingStarted += status => 
        {
            Schedule(() => _loadingOverlay.Show(status));
        };
        _skillsAnalysisPanel.LoadingStatusChanged += status =>
        {
            Schedule(() => _loadingOverlay.UpdateStatus(status));
        };
        _skillsAnalysisPanel.LoadingFinished += () =>
        {
            Schedule(() => _loadingOverlay.Hide());
        };

        // Wire up loading overlay events from session planner panel
        _sessionPlannerPanel.LoadingStarted += status =>
        {
            Schedule(() => _loadingOverlay.Show(status));
        };
        _sessionPlannerPanel.LoadingStatusChanged += status =>
        {
            Schedule(() => _loadingOverlay.UpdateStatus(status));
        };
        _sessionPlannerPanel.LoadingFinished += () =>
        {
            Schedule(() => _loadingOverlay.Hide());
        };

        // Pass skill trends from analysis to session planner
        _skillsAnalysisPanel.TrendsUpdated += trends =>
        {
            _sessionPlannerPanel.SetTrends(trends);
        };

        var splitContainer = new SplitTabContainer(new[]
        {
            new SplitTabItem("Rate Changer", _rateChangerPanel),
            new SplitTabItem("Mods", _modSelectionPanel),
            new SplitTabItem("Session Tracker", _sessionTrackerPanel),
            new SplitTabItem("Session History", _sessionHistoryPanel),
            new SplitTabItem("Skills Analysis", _skillsAnalysisPanel),
            new SplitTabItem("Session Planner", _sessionPlannerPanel)
        })
        {
            RelativeSizeAxes = Axes.Both
        };

        return new Container
        {
            RelativeSizeAxes = Axes.Both,
            Child = splitContainer
        };
    }

    private Container CreateMappingTab()
    {
        // Create panels for mapping features (auto-sized via constructors)
        _functionPanel = new FunctionButtonPanel();
        _offsetPanel = new OffsetInputPanel();
        _bulkRateChangerPanel = new BulkRateChangerPanel();
        _marathonCreatorPanel = new MarathonCreatorPanel();

        // Wire up marathon creator events
        _marathonCreatorPanel.CreateMarathonRequested += OnCreateMarathonRequested;
        _marathonCreatorPanel.RecalculateMsdRequested += OnRecalculateMsdRequested;
        _marathonCreatorPanel.LoadingStarted += status =>
        {
            Schedule(() => _loadingOverlay.Show(status));
        };
        _marathonCreatorPanel.LoadingStatusChanged += status =>
        {
            Schedule(() => _loadingOverlay.UpdateStatus(status));
        };
        _marathonCreatorPanel.LoadingFinished += () =>
        {
            Schedule(() => _loadingOverlay.Hide());
        };

        // Combine BPM Analysis and Normalize SV into one panel
        var timingToolsContent = new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 10),
            Children = new Drawable[]
            {
                _functionPanel,
                _offsetPanel
            }
        };

        var splitContainer = new SplitTabContainer(new[]
        {
            new SplitTabItem("Timing Tools", timingToolsContent),
            new SplitTabItem("Bulk Rates", _bulkRateChangerPanel),
            new SplitTabItem("Marathon", _marathonCreatorPanel)
        })
        {
            RelativeSizeAxes = Axes.Both
        };

        return new Container
        {
            RelativeSizeAxes = Axes.Both,
            Child = splitContainer
        };
    }

    private Container CreateSettingsTab()
    {
        return new Container
        {
            RelativeSizeAxes = Axes.Both,
            Child = new BasicScrollContainer
            {
                RelativeSizeAxes = Axes.Both,
                ClampExtension = 200,
                ScrollbarVisible = true,
                Child = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 16),
                    Padding = new MarginPadding { Top = 10, Bottom = 40},
                    Children = new Drawable[]
                    {
                        // UI Scale settings
                        new UIScalePanel
                        {
                            RelativeSizeAxes = Axes.X
                        },
                        // Overlay mode toggle
                        new OverlayModePanel
                        {
                            RelativeSizeAxes = Axes.X
                        },
                        new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(250, 0),
                            Padding = new MarginPadding { Top = 10, Bottom = 40},
                            Children = new Drawable[]
                            {
                                
                                // Overlay position offset
                                new OverlayPositionPanel
                                {
                                    RelativeSizeAxes = Axes.X
                                },
                                // Replay analysis window settings
                                new ReplayAnalysisSettingsPanel
                                {
                                    RelativeSizeAxes = Axes.X
                                },
                            }
                        },
                        
                        // MinaCalc version selector
                        new MinaCalcVersionPanel
                        {
                            RelativeSizeAxes = Axes.X
                        },
                        // Map indexing controls
                        new MapIndexingPanel
                        {
                            RelativeSizeAxes = Axes.X
                        },
                        // Session auto-start settings
                        new SessionAutoStartPanel
                        {
                            RelativeSizeAxes = Axes.X
                        },
                        // Score migration from session copies
                        new ScoreMigrationPanel
                        {
                            RelativeSizeAxes = Axes.X
                        },
                        // Analytics/privacy settings
                        new AnalyticsSettingsPanel
                        {
                            RelativeSizeAxes = Axes.X
                        },
                        // Keybind configuration
                        new KeybindConfigPanel
                        {
                            RelativeSizeAxes = Axes.X
                        },
                    }
                }
            }
        };
    }

    private void TryAttachToOsu()
    {
        if (ProcessDetector.TryAttachToOsu())
        {
            var info = ProcessDetector.GetProcessInfo();
            _mapInfoDisplay.SetConnected();
            
            // Track analytics
            AptabaseService.TrackOsuConnection(connected: true);

            // Subscribe to file modification events
            ProcessDetector.BeatmapFileModified += OnBeatmapFileModified;

            // Try to find recently modified beatmap
            var recentBeatmap = ProcessDetector.FindRecentlyModifiedBeatmap();
            if (recentBeatmap != null)
            {
                LoadBeatmap(recentBeatmap);
            }
            else
            {
                // Try to get beatmap from window title
                var titleBeatmap = ProcessDetector.GetBeatmapFromWindowTitle();
                if (titleBeatmap != null)
                {
                    LoadBeatmap(titleBeatmap);
                }
            }
        }
        else
        {
            _mapInfoDisplay.SetNotConnected();
        }
    }

    private void OnBeatmapFileModified(object? sender, string filePath)
    {
        // This is called from a non-UI thread, so we need to schedule it
        Schedule(() =>
        {
            if (_currentOsuFile == null || _currentOsuFile.FilePath != filePath)
            {
                LoadBeatmap(filePath);
            }
        });
    }

    private void OnFileDropped(string filePath)
    {
        LoadBeatmap(filePath);
    }

    /// <summary>
    /// Handles file drop from the game level.
    /// </summary>
    public void HandleFileDrop(string filePath)
    {

    }

    /// <summary>
    /// Handles selection of a recommended map from the skills analysis panel.
    /// </summary>
    private void OnRecommendedMapSelected(MapRecommendation recommendation)
    {
        if (string.IsNullOrEmpty(recommendation.BeatmapPath))
        {
            return;
        }

        if (!File.Exists(recommendation.BeatmapPath))
        {
            return;
        }
        
        // Track analytics
        AptabaseService.TrackRecommendationSelected(recommendation.Focus.ToString());

        // Load the beatmap in this application
        LoadBeatmap(recommendation.BeatmapPath);
        
        // If a rate change is suggested, pre-fill the rate changer
        if (recommendation.SuggestedRate.HasValue && Math.Abs(recommendation.SuggestedRate.Value - 1.0f) > 0.05f)
        {
            _rateChangerPanel.SetRate(recommendation.SuggestedRate.Value);
        }
    }

    private void LoadBeatmap(string filePath)
    {
        try
        {
            _currentOsuFile = FileParser.Parse(filePath);
            _mapInfoDisplay.SetMapInfo(_currentOsuFile);
            _functionPanel.SetEnabled(true);
            _offsetPanel.SetEnabled(true);
            _rateChangerPanel.SetEnabled(true);
            _modSelectionPanel.SetEnabled(true);
            _bulkRateChangerPanel.SetEnabled(true);
            _marathonCreatorPanel.SetCurrentBeatmap(_currentOsuFile);
            _marathonCreatorPanel.SetEnabled(true);
            
            // Get dominant BPM and pass to rate changer panel
            var dominantBpm = GetDominantBpm(_currentOsuFile);
            _rateChangerPanel.SetCurrentMapBpm(dominantBpm);
            
            // Update OD/HP sliders with current map values
            _rateChangerPanel.SetMapDifficultyValues(_currentOsuFile.OverallDifficulty, _currentOsuFile.HPDrainRate);
            _bulkRateChangerPanel.SetMapDifficultyValues(_currentOsuFile.OverallDifficulty, _currentOsuFile.HPDrainRate);
            
            // Update rate changer preview
            UpdateRatePreview(1.0, RateChanger.DefaultNameFormat);
        }
        catch (Exception)
        {
            _functionPanel.SetEnabled(false);
            _offsetPanel.SetEnabled(false);
            _rateChangerPanel.SetEnabled(false);
            _modSelectionPanel.SetEnabled(false);
            _bulkRateChangerPanel.SetEnabled(false);
            _marathonCreatorPanel.SetCurrentBeatmap(null);
            _marathonCreatorPanel.SetEnabled(false);
        }
    }

    /// <summary>
    /// Gets the dominant BPM from the current osu file's timing points.
    /// </summary>
    private double GetDominantBpm(OsuFile osuFile)
    {
        var uninherited = osuFile.TimingPoints.Where(tp => tp.Uninherited && tp.BeatLength > 0).ToList();
        if (uninherited.Count == 0) return 120;
        if (uninherited.Count == 1) return uninherited[0].Bpm;

        // Find BPM with longest duration
        var bpmDurations = new Dictionary<double, double>();
        for (int i = 0; i < uninherited.Count; i++)
        {
            var bpm = Math.Round(uninherited[i].Bpm, 1);
            var duration = i < uninherited.Count - 1 
                ? uninherited[i + 1].Time - uninherited[i].Time 
                : 60000;
            if (!bpmDurations.ContainsKey(bpm)) bpmDurations[bpm] = 0;
            bpmDurations[bpm] += duration;
        }

        return bpmDurations.OrderByDescending(kvp => kvp.Value).First().Key;
    }

    private async void OnAnalyzeBpmClicked()
    {
        if (_currentOsuFile == null)
        {
            return;
        }

        // Capture the BPM factor before starting background task
        _pendingBpmFactor = _functionPanel.SelectedBpmFactor;
        var factorLabel = _pendingBpmFactor.GetLabel();
        
        // Track analytics
        AptabaseService.TrackBpmAnalysis(factorLabel);

        _loadingOverlay.Show($"Analyzing BPM ({factorLabel})...");
        SetAllPanelsEnabled(false);

        try
        {
            await Task.Run(() => PerformBpmAnalysis());
        }
        catch (Exception)
        {
        }
        finally
        {
            _loadingOverlay.Hide();
            SetAllPanelsEnabled(true);
        }
    }

    private void PerformBpmAnalysis()
    {
        if (_currentOsuFile == null) return;

        var audioExtractor = new AudioExtractor();
        
        // Use the local bpm.exe from tools directory
        if (!ToolPaths.BpmExeExists)
        {
            throw new FileNotFoundException($"bpm.exe not found at {ToolPaths.BpmExe}. Run build.ps1 to copy tools.");
        }

        var bpmAnalyzer = new BpmAnalyzer(ToolPaths.BpmExe);
        var timingConverter = new TimingPointConverter();
        var fileWriter = new OsuFileWriter();

        // Get audio path
        var audioPath = audioExtractor.GetAudioPath(_currentOsuFile);
        Schedule(() => 
        {
            _loadingOverlay.UpdateStatus($"Analyzing: {Path.GetFileName(audioPath)}");
        });

        // Run BPM analysis
        var bpmResult = bpmAnalyzer.Analyze(audioPath, includeAverage: true);
        Logger.Info($"[Analysis] Got {bpmResult.Beats.Count} beats from bpm.exe");
        
        // Apply BPM factor
        var factor = _pendingBpmFactor.GetMultiplier();
        if (Math.Abs(factor - 1.0) > 0.001)
        {
            Logger.Info($"[Analysis] Applying BPM factor: {_pendingBpmFactor.GetLabel()} ({factor}x)");
            foreach (var beat in bpmResult.Beats)
            {
                beat.Bpm *= factor;
            }
            if (bpmResult.AverageBpm.HasValue)
                bpmResult.AverageBpm *= factor;
            if (bpmResult.EstimatedTempo.HasValue)
                bpmResult.EstimatedTempo *= factor;
        }
        
        var factorLabel = _pendingBpmFactor.GetLabel();
        Schedule(() => 
        {
            _loadingOverlay.UpdateStatus($"Converting {bpmResult.Beats.Count} beats to timing points ({factorLabel})...");
        });

        // Convert to timing points
        var newTimingPoints = timingConverter.Convert(bpmResult);
        var stats = timingConverter.GetStats(bpmResult, newTimingPoints);
        Logger.Info($"[Analysis] Converted to {newTimingPoints.Count} timing points");

        // Merge with existing inherited timing points
        var mergedTimingPoints = fileWriter.MergeTimingPoints(_currentOsuFile.TimingPoints, newTimingPoints);

        // Write back to file
        Schedule(() => _loadingOverlay.UpdateStatus("Writing changes to file..."));
        fileWriter.Write(_currentOsuFile, mergedTimingPoints);

        // Update display
        Schedule(() =>
        {
            var factorInfo = _pendingBpmFactor != BpmFactor.Normal 
                ? $" ({_pendingBpmFactor.GetLabel()})" 
                : "";
            LoadBeatmap(_currentOsuFile.FilePath);
        });
    }

    private async void OnNormalizeSvClicked()
    {
        if (_currentOsuFile == null)
        {
            return;
        }
        
        // Track analytics
        AptabaseService.TrackSvNormalization();

        _loadingOverlay.Show("Normalizing SV...");
        SetAllPanelsEnabled(false);

        try
        {
            await Task.Run(() => PerformSvNormalization());
        }
        catch (Exception)
        {

        }
        finally
        {
            _loadingOverlay.Hide();
            SetAllPanelsEnabled(true);
        }
    }

    private void PerformSvNormalization()
    {
        if (_currentOsuFile == null) return;

        var svNormalizer = new SvNormalizer();
        var fileWriter = new OsuFileWriter();

        var existingTimingPoints = _currentOsuFile.TimingPoints;
        var uninheritedCount = existingTimingPoints.Count(tp => tp.Uninherited);
        
        if (uninheritedCount <= 1)
        {
            return;
        }

        Schedule(() => _loadingOverlay.UpdateStatus($"Normalizing {uninheritedCount} BPM sections..."));

        // Determine base BPM
        var uninherited = existingTimingPoints.Where(tp => tp.Uninherited).OrderBy(tp => tp.Time).ToList();
        double baseBpm = uninherited.Count > 0 ? uninherited[0].Bpm : 120;
        
        if (uninherited.Count > 1)
        {
            var bpmDurations = new Dictionary<double, double>();
            for (int i = 0; i < uninherited.Count; i++)
            {
                double bpm = Math.Round(uninherited[i].Bpm, 1);
                double duration = i < uninherited.Count - 1 
                    ? uninherited[i + 1].Time - uninherited[i].Time 
                    : 60000;
                if (!bpmDurations.ContainsKey(bpm)) bpmDurations[bpm] = 0;
                bpmDurations[bpm] += duration;
            }
            baseBpm = bpmDurations.OrderByDescending(kvp => kvp.Value).First().Key;
        }

        var normalizedTimingPoints = svNormalizer.Normalize(existingTimingPoints, baseBpm);
        var stats = svNormalizer.GetStats(existingTimingPoints, normalizedTimingPoints, baseBpm);

        Schedule(() => _loadingOverlay.UpdateStatus("Writing changes to file..."));
        fileWriter.Write(_currentOsuFile, normalizedTimingPoints);

        Schedule(() =>
        {
            var removedInfo = stats.InheritedPointsRemoved > 0 
                ? $"Removed {stats.InheritedPointsRemoved} SV points. " 
                : "";
            LoadBeatmap(_currentOsuFile.FilePath);
        });
    }

    private async void OnApplyOffsetClicked(double offsetMs)
    {
        if (_currentOsuFile == null)
        {
            return;
        }

        if (Math.Abs(offsetMs) < 0.001)
        {
            return;
        }
        
        // Track analytics
        AptabaseService.TrackOffsetApplied(offsetMs);

        _loadingOverlay.Show($"Applying {offsetMs:+0.##;-0.##;0}ms offset...");
        SetAllPanelsEnabled(false);

        try
        {
            await Task.Run(() => PerformOffsetChange(offsetMs));
        }
        catch (Exception)
        {
        }
        finally
        {
            _loadingOverlay.Hide();
            SetAllPanelsEnabled(true);
        }
    }

    private void PerformOffsetChange(double offsetMs)
    {
        if (_currentOsuFile == null) return;

        var offsetChanger = new OffsetChanger();
        var fileWriter = new OsuFileWriter();

        var existingTimingPoints = _currentOsuFile.TimingPoints;
        var modifiedTimingPoints = offsetChanger.ApplyOffset(existingTimingPoints, offsetMs);
        var stats = offsetChanger.GetStats(existingTimingPoints, modifiedTimingPoints, offsetMs);

        Schedule(() => _loadingOverlay.UpdateStatus("Writing changes to file..."));
        fileWriter.Write(_currentOsuFile, modifiedTimingPoints);

        Schedule(() =>
        {
            _offsetPanel.Reset();
            LoadBeatmap(_currentOsuFile.FilePath);
        });
    }

    private void OnRatePreviewRequested(double rate, string format)
    {
        UpdateRatePreview(rate, format);
    }

    private void OnRateChangerFormatChanged(string format)
    {
        // Save the new format to settings
        UserSettingsService.Settings.RateChangerFormat = format;
        Task.Run(async () => await UserSettingsService.SaveAsync());
    }

    private void OnRateChangerPitchAdjustChanged(bool pitchAdjust)
    {
        // Sync to bulk rate changer panel
        _bulkRateChangerPanel.PitchAdjust = pitchAdjust;
        
        // Save the setting
        UserSettingsService.Settings.RateChangerPitchAdjust = pitchAdjust;
        Task.Run(async () => await UserSettingsService.SaveAsync());
    }

    private void OnBulkRateChangerPitchAdjustChanged(bool pitchAdjust)
    {
        // Sync to rate changer panel
        _rateChangerPanel.PitchAdjust = pitchAdjust;
        
        // Save the setting
        UserSettingsService.Settings.RateChangerPitchAdjust = pitchAdjust;
        Task.Run(async () => await UserSettingsService.SaveAsync());
    }

    private void OnTabChanged(int tabIndex)
    {
        // Track analytics
        var tabNames = new[] { "Gameplay", "Mapping", "Settings" };
        var tabName = tabIndex >= 0 && tabIndex < tabNames.Length ? tabNames[tabIndex] : "Unknown";
    }

    private async void OnApplyModClicked(IMod mod)
    {
        if (_currentOsuFile == null)
        {
            _modSelectionPanel.SetStatus("No beatmap loaded");
            return;
        }

        _loadingOverlay.Show($"Applying {mod.Name}...");
        SetAllPanelsEnabled(false);

        try
        {
            var result = await ModService.ApplyModAsync(
                mod,
                _currentOsuFile,
                null,
                status => Schedule(() => _loadingOverlay.UpdateStatus(status)));

            if (result.Success && result.OutputFilePath != null)
            {
                Schedule(() =>
                {
                    _modSelectionPanel.SetStatus($"Created: {Path.GetFileName(result.OutputFilePath)}");
                    LoadBeatmap(result.OutputFilePath);
                });
            }
            else
            {
                Schedule(() =>
                {
                    _modSelectionPanel.SetStatus(result.ErrorMessage ?? "Mod failed");
                });
            }
        }
        catch (Exception ex)
        {
            Schedule(() =>
            {
                _modSelectionPanel.SetStatus($"Error: {ex.Message}");
            });
        }
        finally
        {
            Schedule(() =>
            {
                _loadingOverlay.Hide();
                SetAllPanelsEnabled(true);
            });
        }
    }

    private void UpdateRatePreview(double rate, string format)
    {
        if (_currentOsuFile == null)
        {
            _rateChangerPanel.SetPreviewText("(no beatmap loaded)");
            return;
        }

        var rateChanger = new RateChanger();
        var dominantBpm = GetDominantBpm(_currentOsuFile);

        var newBpm = dominantBpm * rate;
        var previewName = rateChanger.FormatDifficultyName(format, _currentOsuFile, rate, newBpm);
        _rateChangerPanel.SetPreviewText(previewName);
    }

    private async void OnApplyRateClicked(double rate, string format, bool pitchAdjust, double customOd, double customHp)
    {
        if (_currentOsuFile == null)
        {
            return;
        }
        
        // Save pitch adjust setting
        UserSettingsService.Settings.RateChangerPitchAdjust = pitchAdjust;
        _ = UserSettingsService.SaveAsync();
        
        // Track analytics
        AptabaseService.TrackRateChange(rate, isBulk: false);

        var rateChanger = new RateChanger();
        _loadingOverlay.Show("Checking ffmpeg...");

        var ffmpegAvailable = await rateChanger.CheckFfmpegAvailableAsync();
        if (!ffmpegAvailable)
        {
            _loadingOverlay.Hide();
            return;
        }

        var pitchMode = pitchAdjust ? "with pitch change" : "preserving pitch";
        _loadingOverlay.Show($"Creating {rate:0.0#}x rate-changed beatmap ({pitchMode})...");
        SetAllPanelsEnabled(false);

        try
        {
            var newOsuPath = await rateChanger.CreateRateChangedBeatmapAsync(
                _currentOsuFile,
                rate,
                format,
                pitchAdjust,
                customOd,
                customHp,
                status => Schedule(() => 
                {
                    _loadingOverlay.UpdateStatus(status);
                }));

            Schedule(() =>
            {
                LoadBeatmap(newOsuPath);
            });
        }
        catch (Exception)
        {
        }
        finally
        {
            Schedule(() =>
            {
                _loadingOverlay.Hide();
                SetAllPanelsEnabled(true);
            });
        }
    }

    private async void OnApplyBulkRateClicked(double minRate, double maxRate, double step, string format, bool pitchAdjust, double customOd, double customHp)
    {
        if (_currentOsuFile == null)
        {
            return;
        }
        
        // Save pitch adjust setting
        UserSettingsService.Settings.RateChangerPitchAdjust = pitchAdjust;
        _ = UserSettingsService.SaveAsync();
        
        // Calculate how many rates will be created
        int rateCount = (int)Math.Floor((maxRate - minRate) / step) + 1;
        
        // Track analytics
        AptabaseService.TrackBulkRateChange(minRate, maxRate, step, rateCount);

        var rateChanger = new RateChanger();
        _loadingOverlay.Show("Checking ffmpeg...");

        var ffmpegAvailable = await rateChanger.CheckFfmpegAvailableAsync();
        if (!ffmpegAvailable)
        {
            _loadingOverlay.Hide();
            return;
        }

        var pitchMode = pitchAdjust ? "with pitch change" : "preserving pitch";
        _loadingOverlay.Show($"Creating rate-changed beatmaps ({minRate:0.0#}x to {maxRate:0.0#}x, {pitchMode})...");
        SetAllPanelsEnabled(false);

        try
        {
            var createdFiles = await rateChanger.CreateBulkRateChangedBeatmapsAsync(
                _currentOsuFile,
                minRate,
                maxRate,
                step,
                format,
                pitchAdjust,
                customOd,
                customHp,
                status => Schedule(() => 
                {
                    _loadingOverlay.UpdateStatus(status);
                }));
        }
        catch (Exception)
        {

        }
        finally
        {
            Schedule(() =>
            {
                _loadingOverlay.Hide();
                SetAllPanelsEnabled(true);
            });
        }
    }

    private async void OnCreateMarathonRequested(List<MarathonEntry> entries, MarathonMetadata metadata)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var marathonService = new MarathonCreatorService();
        _loadingOverlay.Show("Checking ffmpeg...");

        var ffmpegAvailable = await marathonService.CheckFfmpegAvailableAsync();
        if (!ffmpegAvailable)
        {
            _loadingOverlay.Hide();
            return;
        }

        // Use the osu! Songs folder as output directory
        var outputDir = ProcessDetector.GetSongsFolder();
        if (string.IsNullOrEmpty(outputDir) || !Directory.Exists(outputDir))
        {
            _loadingOverlay.Hide();
            return;
        }

        _loadingOverlay.Show($"Creating marathon from {entries.Count} maps...");
        SetAllPanelsEnabled(false);

        try
        {
            var result = await marathonService.CreateMarathonAsync(
                entries,
                metadata,
                outputDir,
                status => Schedule(() =>
                {
                    _loadingOverlay.UpdateStatus(status);
                }));

            Schedule(() =>
            {
                if (result.Success && result.OutputPath != null)
                {
                    var duration = TimeSpan.FromMilliseconds(result.TotalDurationMs);
                    LoadBeatmap(result.OutputPath);

                    // Track analytics
                    var mapCount = entries.Count(e => !e.IsPause);
                    var pauseCount = entries.Count(e => e.IsPause);
                    AptabaseService.TrackMarathonCreated(mapCount, pauseCount, duration.TotalMinutes);
                }
            });
        }
        catch (Exception)
        {

        }
        finally
        {
            Schedule(() =>
            {
                _loadingOverlay.Hide();
                SetAllPanelsEnabled(true);
            });
        }
    }

    private async void OnRecalculateMsdRequested(List<MarathonEntry> entries)
    {
        // Filter out pause entries - they don't have MSD
        var mapEntries = entries.Where(e => !e.IsPause).ToList();
        
        if (mapEntries.Count == 0)
        {
            return;
        }

        if (!ToolPaths.MsdCalculatorExists)
        {
            return;
        }

        _loadingOverlay.Show($"Calculating MSD for {mapEntries.Count} maps...");
        SetAllPanelsEnabled(false);

        try
        {
            var analyzer = new MsdAnalyzer(ToolPaths.MsdCalculator);

            for (int i = 0; i < mapEntries.Count; i++)
            {
                var entry = mapEntries[i];
                Schedule(() => _loadingOverlay.UpdateStatus($"Calculating MSD for [{entry.Version}] ({i + 1}/{mapEntries.Count})..."));

                try
                {
                    var result = await analyzer.AnalyzeSingleRateAsync(entry.OsuFile!.FilePath, (float)entry.Rate);
                    entry.MsdValues = result?.Scores;
                }
                catch (Exception ex)
                {
                    Logger.Info($"[MSD] Failed to calculate MSD for {entry.Title}: {ex.Message}");
                    entry.MsdValues = null;
                }
            }

            Schedule(() =>
            {
                _marathonCreatorPanel.RefreshList();
            });
        }
        catch (Exception)
        {

        }
        finally
        {
            Schedule(() =>
            {
                _loadingOverlay.Hide();
                SetAllPanelsEnabled(true);
            });
        }
    }

    private void SetAllPanelsEnabled(bool enabled)
    {
        _functionPanel.SetEnabled(enabled);
        _offsetPanel.SetEnabled(enabled);
        _rateChangerPanel.SetEnabled(enabled);
        _modSelectionPanel.SetEnabled(enabled);
        _bulkRateChangerPanel.SetEnabled(enabled);
        _marathonCreatorPanel.SetEnabled(enabled);
    }

    protected override void Update()
    {
        base.Update();

        // Keep background visible for color keying - don't set alpha to 0
        // The color key will make RGB(25, 25, 30) transparent
        if (_backgroundBox != null)
        {
            // Keep background visible so color keying can work
            _backgroundBox.Alpha = 1f;
        }

        // Show/hide title bar based on overlay mode
        // Default to visible (alpha = 1) if overlay service is not available
        if (_titleBar != null)
        {
            if (OverlayService != null)
            {
                _titleBar.Alpha = OverlayService.IsOverlayMode ? 0f : 1f;
            }
            else
            {
                // If overlay service not available yet, keep it visible
                _titleBar.Alpha = 1f;
            }
        }

        // Periodically check for beatmap changes
        _beatmapCheckTimer += Clock.ElapsedFrameTime;
        if (_beatmapCheckTimer >= BeatmapCheckInterval)
        {
            _beatmapCheckTimer = 0;
            CheckForBeatmapChanges();
        }
    }

    // Track last detected mods for change detection
    private float _lastDetectedRate = 1.0f;

    private void CheckForBeatmapChanges()
    {
        if (!ProcessDetector.IsOsuRunning)
        {
            // Show not connected overlay if no beatmap is loaded
            if (_currentOsuFile == null)
            {
                _mapInfoDisplay.SetNotConnected();
            }
            
            // Try to reattach if osu! was closed and reopened
            if (ProcessDetector.TryAttachToOsu())
            {
                var info = ProcessDetector.GetProcessInfo();
                _mapInfoDisplay.SetConnected();
                ProcessDetector.BeatmapFileModified += OnBeatmapFileModified;
            }
            return;
        }
        
        // osu! is running, ensure overlay is hidden
        _mapInfoDisplay.SetConnected();

        string? detectedBeatmap = null;

        // Try memory reading first (song select / gameplay)
        detectedBeatmap = ProcessDetector.GetBeatmapFromMemory();

        // Load if we found a beatmap and it's different from current
        if (detectedBeatmap != null && detectedBeatmap != _lastDetectedBeatmap)
        {
            _lastDetectedBeatmap = detectedBeatmap;
            
            if (_currentOsuFile == null || _currentOsuFile.FilePath != detectedBeatmap)
            {
                LoadBeatmap(detectedBeatmap);
            }
        }

        // Check for mod changes (DT/HT) on the same map
        float currentRate = ProcessDetector.GetCurrentRateFromMods();
        if (Math.Abs(currentRate - _lastDetectedRate) > 0.01f)
        {
            _lastDetectedRate = currentRate;
            
            // Refresh MSD analysis with new rate if we have a map loaded
            if (_currentOsuFile != null)
            {
                Logger.Info($"[MainScreen] Mod rate changed to {currentRate:F2}x, refreshing MSD");
                _mapInfoDisplay.RefreshMsdAnalysis();
            }
        }
    }
}
