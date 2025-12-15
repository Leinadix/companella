using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;
using OsuMappingHelper.Components;
using OsuMappingHelper.Models;
using OsuMappingHelper.Services;

namespace OsuMappingHelper.Screens;

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
    private UserSettingsService UserSettingsService { get; set; } = null!;

    // Header components
    private MapInfoDisplay _mapInfoDisplay = null!;
    
    // Tab container
    private TabContainer _tabContainer = null!;
    
    // Gameplay tab components
    private RateChangerPanel _rateChangerPanel = null!;
    
    // Mapping tab components
    private FunctionButtonPanel _functionPanel = null!;
    private OffsetInputPanel _offsetPanel = null!;
    private BulkRateChangerPanel _bulkRateChangerPanel = null!;
    
    // Footer components
    private StatusDisplay _statusDisplay = null!;
    private DropZone _dropZone = null!;
    private AppFooter _appFooter = null!;
    private LoadingOverlay _loadingOverlay = null!;
    
    // Window decoration
    private CustomTitleBar _titleBar = null!;
    
    // Background box for transparency control
    private Box _backgroundBox = null!;

    private OsuFile? _currentOsuFile;
    private string? _lastDetectedBeatmap;
    private double _beatmapCheckTimer;
    private const double BEATMAP_CHECK_INTERVAL = 1000; // Check every 1 second
    
    // Store BPM factor for use in background task
    private Models.BpmFactor _pendingBpmFactor;

    [BackgroundDependencyLoader]
    private void load()
    {
        // Create tab contents
        var gameplayTabContent = CreateGameplayTab();
        var mappingTabContent = CreateMappingTab();
        var settingsTabContent = CreateSettingsTab();

        InternalChildren = new Drawable[]
        {
            // Custom osu!-styled title bar (on top)
            _titleBar = new CustomTitleBar
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Alpha = 1f // Visible by default, will be hidden in overlay mode
            },
            // Dark background (transparent in overlay mode)
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
                    new Dimension(GridSizeMode.Absolute, 300),  // Map info header (2.5x bigger)
                    new Dimension(GridSizeMode.Relative, 1f),   // Tab container (takes remaining space)
                    new Dimension(GridSizeMode.Absolute, 100),  // Status display
                    new Dimension(GridSizeMode.Absolute, 50),   // Drop zone
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
                    },
                    new Drawable[]
                    {
                        _statusDisplay = new StatusDisplay
                        {
                            RelativeSizeAxes = Axes.Both
                        }
                    },
                    new Drawable[]
                    {
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Padding = new MarginPadding { Top = 10 },
                            Child = _dropZone = new DropZone
                            {
                                RelativeSizeAxes = Axes.Both
                            }
                        }
                    }
                }
            },
            // App footer (bottom of screen)
            _appFooter = new AppFooter(),
            // Loading overlay (on top of everything)
            _loadingOverlay = new LoadingOverlay()
        };

        // Wire up events
        _dropZone.FileDropped += OnFileDropped;
        _functionPanel.AnalyzeBpmClicked += OnAnalyzeBpmClicked;
        _functionPanel.NormalizeSvClicked += OnNormalizeSvClicked;
        _offsetPanel.ApplyOffsetClicked += OnApplyOffsetClicked;
        _rateChangerPanel.ApplyRateClicked += OnApplyRateClicked;
        _rateChangerPanel.PreviewRequested += OnRatePreviewRequested;
        _rateChangerPanel.FormatChanged += OnRateChangerFormatChanged;
        _bulkRateChangerPanel.ApplyBulkRateClicked += OnApplyBulkRateClicked;

        // Restore saved rate changer format
        var savedFormat = UserSettingsService.Settings.RateChangerFormat;
        if (!string.IsNullOrWhiteSpace(savedFormat))
        {
            _rateChangerPanel.SetFormat(savedFormat);
        }

        // Try to attach to osu! process
        TryAttachToOsu();
    }

    private Container CreateGameplayTab()
    {
        return new Container
        {
            RelativeSizeAxes = Axes.Both,
            Child = new BasicScrollContainer
            {
                RelativeSizeAxes = Axes.Both,
                ClampExtension = 0,
                Child = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 8),
                    Children = new Drawable[]
                    {
                        // Rate changer panel
                        _rateChangerPanel = new RateChangerPanel
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 220
                        }
                    }
                }
            }
        };
    }

    private Container CreateMappingTab()
    {
        return new Container
        {
            RelativeSizeAxes = Axes.Both,
            Child = new BasicScrollContainer
            {
                RelativeSizeAxes = Axes.Both,
                ClampExtension = 0,
                Child = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 8),
                    Children = new Drawable[]
                    {
                        // Function buttons (Analyze BPM, Normalize SV)
                        _functionPanel = new FunctionButtonPanel
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 60
                        },
                        // Offset input panel
                        _offsetPanel = new OffsetInputPanel
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 85
                        },
                        // Bulk rate changer
                        _bulkRateChangerPanel = new BulkRateChangerPanel
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 210
                        }
                    }
                }
            }
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
                ClampExtension = 0,
                Child = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 16),
                    Padding = new MarginPadding { Top = 10 },
                    Children = new Drawable[]
                    {
                        // Overlay position offset
                        new OverlayPositionPanel
                        {
                            RelativeSizeAxes = Axes.X
                        },
                        // Keybind configuration
                        new KeybindConfigPanel
                        {
                            RelativeSizeAxes = Axes.X
                        }
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
            _statusDisplay.SetStatus($"Connected to osu! (PID: {info?.ProcessId})", StatusType.Success);

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
            _statusDisplay.SetStatus("osu! not detected. Drop a .osu file to get started.", StatusType.Warning);
        }
    }

    private void OnBeatmapFileModified(object? sender, string filePath)
    {
        // This is called from a non-UI thread, so we need to schedule it
        Schedule(() =>
        {
            if (_currentOsuFile == null || _currentOsuFile.FilePath != filePath)
            {
                _statusDisplay.SetStatus($"Detected: {Path.GetFileName(filePath)}", StatusType.Info);
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
        _dropZone.HandleFileDrop(filePath);
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
            _bulkRateChangerPanel.SetEnabled(true);
            _statusDisplay.SetStatus($"Loaded: {_currentOsuFile.DisplayName}", StatusType.Success);
            
            // Update rate changer preview
            UpdateRatePreview(1.0, RateChanger.DefaultNameFormat);
        }
        catch (Exception ex)
        {
            _statusDisplay.SetStatus($"Failed to load beatmap: {ex.Message}", StatusType.Error);
            _functionPanel.SetEnabled(false);
            _offsetPanel.SetEnabled(false);
            _rateChangerPanel.SetEnabled(false);
            _bulkRateChangerPanel.SetEnabled(false);
        }
    }

    private async void OnAnalyzeBpmClicked()
    {
        if (_currentOsuFile == null)
        {
            _statusDisplay.SetStatus("No beatmap loaded.", StatusType.Error);
            return;
        }

        // Capture the BPM factor before starting background task
        _pendingBpmFactor = _functionPanel.SelectedBpmFactor;
        var factorLabel = _pendingBpmFactor.GetLabel();

        _loadingOverlay.Show($"Analyzing BPM ({factorLabel})...");
        _statusDisplay.SetStatus($"Analyzing BPM ({factorLabel})... This may take a few minutes.", StatusType.Info);
        SetAllPanelsEnabled(false);

        try
        {
            await Task.Run(() => PerformBpmAnalysis());
        }
        catch (Exception ex)
        {
            _statusDisplay.SetStatus($"BPM analysis failed: {ex.Message}", StatusType.Error);
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
        
        // Use the local bpm.py from tools directory
        if (!ToolPaths.BpmScriptExists)
        {
            throw new FileNotFoundException($"bpm.py not found at {ToolPaths.BpmScript}. Run build.ps1 to copy tools.");
        }

        var bpmAnalyzer = new BpmAnalyzer(ToolPaths.BpmScript);
        var timingConverter = new TimingPointConverter();
        var fileWriter = new OsuFileWriter();

        // Get audio path
        var audioPath = audioExtractor.GetAudioPath(_currentOsuFile);
        Schedule(() => 
        {
            _loadingOverlay.UpdateStatus($"Analyzing: {Path.GetFileName(audioPath)}");
            _statusDisplay.SetStatus($"Analyzing: {Path.GetFileName(audioPath)}", StatusType.Info);
        });

        // Run BPM analysis
        var bpmResult = bpmAnalyzer.Analyze(audioPath, includeAverage: true);
        Console.WriteLine($"[Analysis] Got {bpmResult.Beats.Count} beats from bpm.py");
        
        // Apply BPM factor
        var factor = _pendingBpmFactor.GetMultiplier();
        if (Math.Abs(factor - 1.0) > 0.001)
        {
            Console.WriteLine($"[Analysis] Applying BPM factor: {_pendingBpmFactor.GetLabel()} ({factor}x)");
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
            _statusDisplay.SetStatus($"Found {bpmResult.Beats.Count} beats ({factorLabel}). Converting to timing points...", StatusType.Info);
        });

        // Convert to timing points
        var newTimingPoints = timingConverter.Convert(bpmResult);
        var stats = timingConverter.GetStats(bpmResult, newTimingPoints);
        Console.WriteLine($"[Analysis] Converted to {newTimingPoints.Count} timing points");

        // Merge with existing inherited timing points
        var mergedTimingPoints = fileWriter.MergeTimingPoints(_currentOsuFile.TimingPoints, newTimingPoints);

        // Write back to file
        Schedule(() => _loadingOverlay.UpdateStatus("Writing changes to file..."));
        fileWriter.Write(_currentOsuFile, mergedTimingPoints);

        // Update display
        Schedule(() =>
        {
            var factorInfo = _pendingBpmFactor != Models.BpmFactor.Normal 
                ? $" ({_pendingBpmFactor.GetLabel()})" 
                : "";
            _statusDisplay.SetStatus(
                $"Done! Created {stats.TimingPointsCreated} timing points{factorInfo}. BPM range: {stats.MinBpm:F1} - {stats.MaxBpm:F1}",
                StatusType.Success);
            LoadBeatmap(_currentOsuFile.FilePath);
        });
    }

    private async void OnNormalizeSvClicked()
    {
        if (_currentOsuFile == null)
        {
            _statusDisplay.SetStatus("No beatmap loaded.", StatusType.Error);
            return;
        }

        _loadingOverlay.Show("Normalizing SV...");
        _statusDisplay.SetStatus("Normalizing scroll velocity...", StatusType.Info);
        SetAllPanelsEnabled(false);

        try
        {
            await Task.Run(() => PerformSvNormalization());
        }
        catch (Exception ex)
        {
            _statusDisplay.SetStatus($"SV normalization failed: {ex.Message}", StatusType.Error);
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
            Schedule(() => _statusDisplay.SetStatus("No BPM changes found - normalization not needed.", StatusType.Warning));
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
            _statusDisplay.SetStatus(
                $"Done! {removedInfo}Normalized to {stats.BaseBpm:F0} BPM. SV range: {stats.MinSv:F2}x - {stats.MaxSv:F2}x",
                StatusType.Success);
            LoadBeatmap(_currentOsuFile.FilePath);
        });
    }

    private async void OnApplyOffsetClicked(double offsetMs)
    {
        if (_currentOsuFile == null)
        {
            _statusDisplay.SetStatus("No beatmap loaded.", StatusType.Error);
            return;
        }

        if (Math.Abs(offsetMs) < 0.001)
        {
            _statusDisplay.SetStatus("Offset is zero - no changes needed.", StatusType.Warning);
            return;
        }

        _loadingOverlay.Show($"Applying {offsetMs:+0.##;-0.##;0}ms offset...");
        SetAllPanelsEnabled(false);

        try
        {
            await Task.Run(() => PerformOffsetChange(offsetMs));
        }
        catch (Exception ex)
        {
            _statusDisplay.SetStatus($"Offset change failed: {ex.Message}", StatusType.Error);
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
            _statusDisplay.SetStatus(
                $"Done! Applied {offsetMs:+0.##;-0.##;0}ms offset to {stats.TimingPointsModified} timing points.",
                StatusType.Success);
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

    private void UpdateRatePreview(double rate, string format)
    {
        if (_currentOsuFile == null)
        {
            _rateChangerPanel.SetPreviewText("(no beatmap loaded)");
            return;
        }

        var rateChanger = new RateChanger();
        var dominantBpm = _currentOsuFile.TimingPoints
            .Where(tp => tp.Uninherited && tp.BeatLength > 0)
            .Select(tp => tp.Bpm)
            .FirstOrDefault();
        if (dominantBpm == 0) dominantBpm = 120;

        var newBpm = dominantBpm * rate;
        var previewName = rateChanger.FormatDifficultyName(format, _currentOsuFile, rate, newBpm);
        _rateChangerPanel.SetPreviewText(previewName);
    }

    private async void OnApplyRateClicked(double rate, string format)
    {
        if (_currentOsuFile == null)
        {
            _statusDisplay.SetStatus("No beatmap loaded.", StatusType.Error);
            return;
        }

        var rateChanger = new RateChanger();
        _loadingOverlay.Show("Checking ffmpeg...");

        var ffmpegAvailable = await rateChanger.CheckFfmpegAvailableAsync();
        if (!ffmpegAvailable)
        {
            _loadingOverlay.Hide();
            _statusDisplay.SetStatus("ffmpeg not found! Please install ffmpeg and add it to PATH.", StatusType.Error);
            return;
        }

        _loadingOverlay.Show($"Creating {rate:0.0#}x rate-changed beatmap...");
        SetAllPanelsEnabled(false);

        try
        {
            var newOsuPath = await rateChanger.CreateRateChangedBeatmapAsync(
                _currentOsuFile,
                rate,
                format,
                status => Schedule(() => 
                {
                    _loadingOverlay.UpdateStatus(status);
                    _statusDisplay.SetStatus(status, StatusType.Info);
                }));

            Schedule(() =>
            {
                _statusDisplay.SetStatus($"Done! Created: {Path.GetFileName(newOsuPath)}", StatusType.Success);
                LoadBeatmap(newOsuPath);
            });
        }
        catch (Exception ex)
        {
            Schedule(() => _statusDisplay.SetStatus($"Rate change failed: {ex.Message}", StatusType.Error));
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

    private async void OnApplyBulkRateClicked(double minRate, double maxRate, double step, string format)
    {
        if (_currentOsuFile == null)
        {
            _statusDisplay.SetStatus("No beatmap loaded.", StatusType.Error);
            return;
        }

        var rateChanger = new RateChanger();
        _loadingOverlay.Show("Checking ffmpeg...");

        var ffmpegAvailable = await rateChanger.CheckFfmpegAvailableAsync();
        if (!ffmpegAvailable)
        {
            _loadingOverlay.Hide();
            _statusDisplay.SetStatus("ffmpeg not found! Please install ffmpeg and add it to PATH.", StatusType.Error);
            return;
        }

        _loadingOverlay.Show($"Creating rate-changed beatmaps ({minRate:0.0#}x to {maxRate:0.0#}x)...");
        SetAllPanelsEnabled(false);

        try
        {
            var createdFiles = await rateChanger.CreateBulkRateChangedBeatmapsAsync(
                _currentOsuFile,
                minRate,
                maxRate,
                step,
                format,
                status => Schedule(() => 
                {
                    _loadingOverlay.UpdateStatus(status);
                    _statusDisplay.SetStatus(status, StatusType.Info);
                }));

            Schedule(() =>
            {
                _statusDisplay.SetStatus(
                    $"Bulk rate change complete! Created {createdFiles.Count} beatmaps.",
                    StatusType.Success);
            });
        }
        catch (Exception ex)
        {
            Schedule(() => _statusDisplay.SetStatus($"Bulk rate change failed: {ex.Message}", StatusType.Error));
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
        _bulkRateChangerPanel.SetEnabled(enabled);
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
        if (_beatmapCheckTimer >= BEATMAP_CHECK_INTERVAL)
        {
            _beatmapCheckTimer = 0;
            CheckForBeatmapChanges();
        }
    }

    private void CheckForBeatmapChanges()
    {
        if (!ProcessDetector.IsOsuRunning)
        {
            // Try to reattach if osu! was closed and reopened
            if (ProcessDetector.TryAttachToOsu())
            {
                var info = ProcessDetector.GetProcessInfo();
                _statusDisplay.SetStatus($"Reconnected to osu! (PID: {info?.ProcessId})", StatusType.Success);
                ProcessDetector.BeatmapFileModified += OnBeatmapFileModified;
            }
            return;
        }

        string? detectedBeatmap = null;
        string source = "";

        // Try memory reading first (song select / gameplay)
        detectedBeatmap = ProcessDetector.GetBeatmapFromMemory();
        if (detectedBeatmap != null)
        {
            source = "Memory";
        }
        else
        {
            // Fallback to window title (editor mode)
            detectedBeatmap = ProcessDetector.GetBeatmapFromWindowTitle();
            if (detectedBeatmap != null)
            {
                source = "Editor";
            }
        }

        // Load if we found a beatmap and it's different from current
        if (detectedBeatmap != null && detectedBeatmap != _lastDetectedBeatmap)
        {
            _lastDetectedBeatmap = detectedBeatmap;
            
            if (_currentOsuFile == null || _currentOsuFile.FilePath != detectedBeatmap)
            {
                LoadBeatmap(detectedBeatmap);
                _statusDisplay.SetStatus($"{source}: {Path.GetFileName(detectedBeatmap)}", StatusType.Success);
            }
        }
    }
}
