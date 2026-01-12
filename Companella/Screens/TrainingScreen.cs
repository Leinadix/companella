using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;
using Companella.Components.Analysis;
using Companella.Components.Layout;
using Companella.Components.Tools;
using Companella.Models.Application;
using Companella.Models.Beatmap;
using Companella.Models.Difficulty;
using Companella.Models.Training;
using Companella.Services.Analysis;
using Companella.Services.Beatmap;
using Companella.Services.Common;
using Companella.Services.Database;
using Companella.Services.Platform;
using Companella.Services.Tools;
using Companella.Components.Misc;

namespace Companella.Screens;

/// <summary>
/// Training screen for refining dans.json with user maps.
/// Uses the same auto-detection and UI layout as MainScreen.
/// </summary>
public partial class TrainingScreen : osu.Framework.Screens.Screen
{
    [Resolved]
    private OsuProcessDetector ProcessDetector { get; set; } = null!;

    [Resolved]
    private OsuFileParser FileParser { get; set; } = null!;

    [Resolved]
    private TrainingDataService TrainingService { get; set; } = null!;

    [Resolved]
    private OsuWindowOverlayService OverlayService { get; set; } = null!;

    // Header components (same as MainScreen)
    private MapInfoDisplay _mapInfoDisplay = null!;
    
    // Window decoration
    private CustomTitleBar _titleBar = null!;
    private DanLevelSelector _danSelector = null!;
    private FunctionButton _saveButton = null!;
    private FunctionButton _mergeButton = null!;
    private SpriteText _statsText = null!;
    private bool _enableExtrapolation = false;
    
    // Store current analysis results for automatic top pattern selection
    private PatternAnalysisResult? _currentPatternResult;
    private double _currentYavsrgRating;

    // Footer components
    private StatusDisplay _statusDisplay = null!;
    private DropZone _dropZone = null!;
    private AppFooter _appFooter = null!;
    private LoadingOverlay _loadingOverlay = null!;

    private OsuFile? _currentOsuFile;
    private string? _lastDetectedBeatmap;
    private double _beatmapCheckTimer;
    private const double BeatmapCheckInterval = 1000;

    // Analysis state
    private CancellationTokenSource? _analysisCancellation;
    private SkillsetScores? _currentMsdScores;

    private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);

    [BackgroundDependencyLoader]
    private void load()
    {
        // Create training tab content
        var trainingTabContent = CreateTrainingTab();

        InternalChildren = new Drawable[]
        {
            // Custom osu!-styled title bar (on top)
            _titleBar = new CustomTitleBar
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Alpha = 1f // Visible by default, will be hidden in overlay mode
            },
            // Dark background
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(25, 25, 30, 255)
            },
            // Main layout using GridContainer for proportional sizing (same as MainScreen)
            new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Left = 15, Right = 15, Top = 47, Bottom = 43 },
                RowDimensions = new[]
                {
                    new Dimension(GridSizeMode.Absolute, 300),  // Map info header
                    new Dimension(GridSizeMode.Relative, 1f),   // Training content
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
                            Child = new TabContainer(
                                new[] { "Training" },
                                new[] { trainingTabContent })
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
        _danSelector.SelectionChanged += _ => UpdateButtonStates();
        _saveButton.Clicked += OnSaveClicked;
        _mergeButton.Clicked += OnMergeClicked;

        // Try to attach to osu! process (same as MainScreen)
        TryAttachToOsu();
    }

    private Container CreateTrainingTab()
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
                        // Training mode header
                        new Container
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 40,
                            Masking = true,
                            CornerRadius = 6,
                            Children = new Drawable[]
                            {
                                new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Colour = new Color4(40, 38, 50, 255)
                                },
                                new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Direction = FillDirection.Horizontal,
                                    Padding = new MarginPadding { Horizontal = 12 },
                                    Children = new Drawable[]
                                    {
                                        new SpriteText
                                        {
                                            Text = "Dan Training Mode",
                                            Font = new FontUsage("", 18, "Bold"),
                                            Colour = _accentColor,
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft
                                        },
                                        new Container { Width = 20 },
                                        _statsText = new SpriteText
                                        {
                                            Text = "Training data: 0 entries",
                                            Font = new FontUsage("", 13),
                                            Colour = new Color4(140, 140, 140, 255),
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft
                                        }
                                    }
                                }
                            }
                        },
                        // Dan level selector
                        _danSelector = new DanLevelSelector
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 110
                        },
                        // Action buttons
                        new Container
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 50,
                            Masking = true,
                            CornerRadius = 6,
                            Children = new Drawable[]
                            {
                                new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Colour = new Color4(35, 33, 43, 255)
                                },
                                new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Direction = FillDirection.Vertical,
                                    Padding = new MarginPadding { Horizontal = 12, Vertical = 8 },
                                    Spacing = new Vector2(0, 8),
                                    Children = new Drawable[]
                                    {
                                        // Buttons row
                                        new FillFlowContainer
                                        {
                                            RelativeSizeAxes = Axes.X,
                                            AutoSizeAxes = Axes.Y,
                                            Direction = FillDirection.Horizontal,
                                            Spacing = new Vector2(12, 0),
                                            Children = new Drawable[]
                                            {
                                                _saveButton = new FunctionButton("Save Training Entry")
                                                {
                                                    Width = 180,
                                                    Height = 34,
                                                    Enabled = false
                                                },
                                                _mergeButton = new FunctionButton("Merge into dans.json")
                                                {
                                                    Width = 180,
                                                    Height = 34,
                                                    Enabled = false
                                                }
                                            }
                                        },
                                        // Extrapolation checkbox
                                        CreateExtrapolationCheckbox()
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        // Initialize training service
        Task.Run(async () =>
        {
            await TrainingService.InitializeAsync();
            Schedule(() =>
            {
                UpdateStats();
                _mergeButton.Enabled = TrainingService.EntryCount > 0;
            });
        });
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
            _statusDisplay.SetStatus($"Loaded: {_currentOsuFile.DisplayName}", StatusType.Success);
            
            // Clear previous selections and difficulty
            _currentPatternResult = null;
            _currentYavsrgRating = 0;
            _danSelector.ClearSelection();
            _danSelector.SetDifficulty(null, null);
            UpdateButtonStates();

            // Run pattern analysis for training selector
            RunPatternAnalysis(_currentOsuFile);
        }
        catch (Exception ex)
        {
            _currentPatternResult = null;
            _currentYavsrgRating = 0;
            _statusDisplay.SetStatus($"Failed to load beatmap: {ex.Message}", StatusType.Error);
        }
    }

    private void RunPatternAnalysis(OsuFile osuFile)
    {
        // Cancel any pending analysis
        _analysisCancellation?.Cancel();
        _analysisCancellation = new CancellationTokenSource();
        var token = _analysisCancellation.Token;

        // Only analyze 4K mania maps
        if (osuFile.Mode != 3 || Math.Abs(osuFile.CircleSize - 4.0) > 0.1)
        {
            return;
        }

        // Run analysis in background
        Task.Run(async () =>
        {
            try
            {
                // Pattern analysis
                var patternFinder = new PatternFinder(osuFile);
                var patternResult = patternFinder.FindAllPatterns(osuFile);

                if (token.IsCancellationRequested)
                    return;

                // MSD analysis (if available) - get full skillset scores
                SkillsetScores? msdScores = null;
                if (ToolPaths.MsdCalculatorExists)
                {
                    try
                    {
                        var msdAnalyzer = new MsdAnalyzer(ToolPaths.MsdCalculator);
                        var msdResult = await msdAnalyzer.AnalyzeSingleRateAsync(osuFile.FilePath, 1.0f);
                        msdScores = msdResult?.Scores;
                    }
                    catch (Exception ex)
                    {
                        Logger.Info($"[Training] MSD analysis failed: {ex.Message}");
                    }
                }

                if (token.IsCancellationRequested)
                    return;

                _currentMsdScores = msdScores;

                // Calculate both MSD and YAVSRG difficulty (separate systems)
                double? msdDifficulty = null;
                double? yavsrgDifficulty = null;
                
                // MSD difficulty (from MSD calculator)
                if (_currentMsdScores != null)
                {
                    msdDifficulty = _currentMsdScores.Overall;
                }
                
                // YAVSRG difficulty (calculated from chart data, independent of MSD)
                try
                {
                    var difficultyService = new InterludeDifficultyService();
                    yavsrgDifficulty = difficultyService.CalculateDifficulty(osuFile, 1.0f);
                }
                catch (Exception ex)
                {
                    Logger.Info($"[Training] YAVSRG difficulty calculation failed: {ex.Message}");
                }

                Schedule(() =>
                {
                    if (!token.IsCancellationRequested)
                    {
                        if (patternResult.Success)
                        {
                            // Store results for automatic top pattern selection
                            _currentPatternResult = patternResult;
                            _currentYavsrgRating = yavsrgDifficulty ?? 0.0;
                        }
                        else
                        {
                            _currentPatternResult = null;
                            _currentYavsrgRating = 0;
                        }
                        
                        // Update difficulty display (both MSD and YAVSRG)
                        _danSelector.SetDifficulty(msdDifficulty, yavsrgDifficulty);
                        
                        UpdateButtonStates();
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Info($"[Training] Analysis failed: {ex.Message}");
                
                if (token.IsCancellationRequested)
                    return;
            }
        }, token);
    }

    protected override void Update()
    {
        base.Update();

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

        // Periodically check for beatmap changes (same as MainScreen)
        _beatmapCheckTimer += Clock.ElapsedFrameTime;
        if (_beatmapCheckTimer >= BeatmapCheckInterval)
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

    private void UpdateButtonStates()
    {
        // Need dan selection + MSD scores + Interlude rating
        bool canSave = _danSelector.HasSelection && _currentMsdScores != null && _currentYavsrgRating > 0;
        _saveButton.Enabled = canSave;
    }

    private async void OnSaveClicked()
    {
        if (!_danSelector.HasSelection)
        {
            _statusDisplay.SetStatus("Select a dan level first", StatusType.Warning);
            return;
        }

        if (_currentMsdScores == null)
        {
            _statusDisplay.SetStatus("No MSD analysis available", StatusType.Warning);
            return;
        }

        if (_currentYavsrgRating <= 0)
        {
            _statusDisplay.SetStatus("No Interlude rating available", StatusType.Warning);
            return;
        }

        var danLabel = _danSelector.SelectedDan!;
        var danIndex = DanLookup.GetIndex(danLabel);
        
        if (danIndex < 0)
        {
            _statusDisplay.SetStatus($"Invalid dan label: {danLabel}", StatusType.Error);
            return;
        }
        
        var sourcePath = _currentOsuFile?.FilePath;

        try
        {
            // Add entry with all MSD skillsets + Interlude rating (using int index)
            TrainingService.AddEntry(_currentMsdScores, _currentYavsrgRating, danIndex, sourcePath);

            // Save to file
            await TrainingService.SaveAsync();

            // Update UI
            _statusDisplay.SetStatus($"Saved MSD {_currentMsdScores.Overall:F2} + Interlude {_currentYavsrgRating:F2} -> Dan {danLabel} (idx={danIndex})", StatusType.Success);
            UpdateStats();

            // Clear dan selection for next entry
            _danSelector.ClearSelection();
            UpdateButtonStates();

            _mergeButton.Enabled = TrainingService.EntryCount > 0;
        }
        catch (Exception ex)
        {
            _statusDisplay.SetStatus($"Failed to save: {ex.Message}", StatusType.Error);
        }
    }

    private async void OnMergeClicked()
    {
        if (TrainingService.EntryCount == 0)
        {
            _statusDisplay.SetStatus("No training data to merge", StatusType.Warning);
            return;
        }

        _loadingOverlay.Show("Merging training data into dans.json...");

        try
        {
            var result = await TrainingService.MergeIntoDansAsync(_enableExtrapolation);

            if (result.Success)
            {
                _statusDisplay.SetStatus(
                    $"Merged! Updated {result.UpdatedDans} dans with MSD + Interlude values",
                    StatusType.Success);
            }
            else
            {
                _statusDisplay.SetStatus($"Merge failed: {result.ErrorMessage}", StatusType.Error);
            }
        }
        catch (Exception ex)
        {
            _statusDisplay.SetStatus($"Merge error: {ex.Message}", StatusType.Error);
        }
        finally
        {
            _loadingOverlay.Hide();
        }
    }

    private Drawable CreateExtrapolationCheckbox()
    {
        var checkBox = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Colour = new Color4(255, 102, 170, 255),
            Alpha = _enableExtrapolation ? 1 : 0
        };

        var checkboxContainer = new Container
        {
            Size = new Vector2(18, 18),
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Masking = true,
            CornerRadius = 3,
            BorderThickness = 2,
            BorderColour = new Color4(100, 100, 100, 255),
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(30, 28, 38, 255)
                },
                checkBox
            }
        };

        var container = new ClickableContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Children = new Drawable[]
            {
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(8, 0),
                    Children = new Drawable[]
                    {
                        checkboxContainer,
                        new SpriteText
                        {
                            Text = "Enable extrapolation (use trends from training data)",
                            Font = new FontUsage("", 12),
                            Colour = new Color4(200, 200, 200, 255),
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft
                        }
                    }
                }
            }
        };

        container.Action += () =>
        {
            _enableExtrapolation = !_enableExtrapolation;
            checkBox.FadeTo(_enableExtrapolation ? 1 : 0, 100);
        };

        return container;
    }

    private void UpdateStats()
    {
        var stats = TrainingService.GetStatistics();
        var statsText = $"Training data: {stats.TotalEntries} entries";

        if (stats.TotalEntries > 0)
        {
            statsText += $" | {stats.UniqueDanLevels} dans";
        }

        _statsText.Text = statsText;
    }
}
