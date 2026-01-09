using osu.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Input.Events;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using osu.Framework.Screens;
using OsuMappingHelper.Components;
using OsuMappingHelper.Screens;
using OsuMappingHelper.Services;
using osuTK.Input;
using System.Diagnostics;
using System.Drawing;

namespace OsuMappingHelper;

/// <summary>
/// Main game class for the Companella! application.
/// </summary>
public partial class OsuMappingHelperGame : Game
{
    private ScreenStack _screenStack = null!;
    private ScaledContentContainer _scaledContainer = null!;
    private DependencyContainer _dependencies = null!;
    private MainScreen? _mainScreen;
    private TrainingScreen? _trainingScreen;

    /// <summary>
    /// Whether the application is running in training mode.
    /// </summary>
    private readonly bool _trainingMode;

    // Settings saving
    private double _settingsSaveTimer;
    private const double SettingsSaveInterval = 2000; // Save every 2 seconds
    private System.Drawing.Size _lastWindowSize;
    private System.Drawing.Point _lastWindowPosition;
    private System.Drawing.Size _targetWindowSize = new System.Drawing.Size(480, 810);

    // Services
    private OsuProcessDetector _processDetector = null!;
    private OsuFileParser _fileParser = null!;
    private OsuFileWriter _fileWriter = null!;
    private AudioExtractor _audioExtractor = null!;
    private TimingPointConverter _timingConverter = null!;
    private DanConfigurationService _danConfigService = null!;
    private TrainingDataService _trainingDataService = null!;
    private UserSettingsService _userSettingsService = null!;
    private OsuWindowOverlayService _overlayService = null!;
    private GlobalHotkeyService _hotkeyService = null!;
    private SquirrelUpdaterService _autoUpdaterService = null!;
    private SessionDatabaseService _sessionDatabaseService = null!;
    private SessionTrackerService _sessionTrackerService = null!;
    
    // Skills analysis services
    private MapsDatabaseService _mapsDatabaseService = null!;
    private SkillsTrendAnalyzer _skillsTrendAnalyzer = null!;
    private MapMmrCalculator _mapMmrCalculator = null!;
    private MapRecommendationService _mapRecommendationService = null!;
    private OsuCollectionService _collectionService = null!;
    private BeatmapApiService _beatmapApiService = null!;
    private ScoreMigrationService _scoreMigrationService = null!;
    
    // Tray icon
    private TrayIconService _trayIconService = null!;
    
    // Analytics
    private AptabaseService _aptabaseService = null!;
    
    // Timing deviation analysis services
    private ReplayParserService _replayParserService = null!;
    private TimingDeviationCalculator _timingDeviationCalculator = null!;
    private HitErrorReaderService _hitErrorReaderService = null!;
    
    // Results overlay for timing deviation display
    private ResultsOverlayWindow? _resultsOverlay;
    
    // Replay analysis window state
    private bool _isInReplayAnalysisMode = false;
    private System.Drawing.Size _savedWindowSizeBeforeAnalysis;
    private System.Drawing.Point _savedWindowPositionBeforeAnalysis;
    
    // Overlay state
    private bool _isWindowVisible = true;
    private bool _wasOsuRunning = false;
    private System.Drawing.Point _savedWindowPosition;
    private DateTimeOffset _lastOverlayToggleTime = DateTimeOffset.MinValue;
    private const double OverlayToggleCooldownMs = 500; // 0.5 second cooldown
    
    // Flag to track if we've performed the startup restart after first connection
    private bool _hasPerformedStartupRestart = false;

    // Callback to close the native splash screen
    private readonly Action? _closeSplashScreen;

    /// <summary>
    /// Creates a new instance of the game.
    /// </summary>
    /// <param name="trainingMode">Whether to start in training mode.</param>
    /// <param name="closeSplashScreen">Optional callback to close the native splash screen.</param>
    public OsuMappingHelperGame(bool trainingMode = false, Action? closeSplashScreen = null)
    {
        _trainingMode = trainingMode;
        _closeSplashScreen = closeSplashScreen;
    }

    protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
    {
        _dependencies = new DependencyContainer(base.CreateChildDependencies(parent));

        // Create and register services
        // Analytics (created early so it can be injected into other services)
        _aptabaseService = new AptabaseService();
        
        _processDetector = new OsuProcessDetector();
        _fileParser = new OsuFileParser();
        _fileWriter = new OsuFileWriter();
        _audioExtractor = new AudioExtractor();
        _timingConverter = new TimingPointConverter();
        _danConfigService = new DanConfigurationService();
        _trainingDataService = new TrainingDataService();
        _userSettingsService = new UserSettingsService();
        
        // Connect settings service to process detector for caching osu! directory
        _processDetector.SetSettingsService(_userSettingsService);
        _overlayService = new OsuWindowOverlayService();
        _hotkeyService = new GlobalHotkeyService();
        _autoUpdaterService = new SquirrelUpdaterService();
        _sessionDatabaseService = new SessionDatabaseService();
        _sessionTrackerService = new SessionTrackerService(_processDetector, _sessionDatabaseService, _aptabaseService);
        _sessionTrackerService.PlayEndedWithPauses += OnPlayEndedWithPauses;
        _sessionTrackerService.ResultsScreenEntered += OnResultsScreenEntered;
        _sessionTrackerService.ResultsScreenExited += OnResultsScreenExited;
        
        // Timing deviation analysis services
        _replayParserService = new ReplayParserService(_processDetector);
        _timingDeviationCalculator = new TimingDeviationCalculator(_fileParser);
        _hitErrorReaderService = new HitErrorReaderService();
        
        // Skills analysis services
        _mapsDatabaseService = new MapsDatabaseService();
        _beatmapApiService = new BeatmapApiService(_userSettingsService);
        _mapsDatabaseService.SetBeatmapApiService(_beatmapApiService);
        _scoreMigrationService = new ScoreMigrationService(_processDetector, _fileParser);
        _skillsTrendAnalyzer = new SkillsTrendAnalyzer(_sessionDatabaseService);
        _mapMmrCalculator = new MapMmrCalculator(_mapsDatabaseService);
        _mapRecommendationService = new MapRecommendationService(_mapsDatabaseService, _mapMmrCalculator, _skillsTrendAnalyzer);
        _collectionService = new OsuCollectionService(_processDetector);
        
        // Tray icon
        _trayIconService = new TrayIconService();

        _dependencies.CacheAs(_processDetector);
        _dependencies.CacheAs(_fileParser);
        _dependencies.CacheAs(_fileWriter);
        _dependencies.CacheAs(_audioExtractor);
        _dependencies.CacheAs(_timingConverter);
        _dependencies.CacheAs(_danConfigService);
        _dependencies.CacheAs(_trainingDataService);
        _dependencies.CacheAs(_userSettingsService);
        _dependencies.CacheAs(_overlayService);
        _dependencies.CacheAs(_hotkeyService);
        _dependencies.CacheAs(_autoUpdaterService);
        _dependencies.CacheAs(_sessionDatabaseService);
        _dependencies.CacheAs(_sessionTrackerService);
        _dependencies.CacheAs(_mapsDatabaseService);
        _dependencies.CacheAs(_skillsTrendAnalyzer);
        _dependencies.CacheAs(_mapMmrCalculator);
        _dependencies.CacheAs(_mapRecommendationService);
        _dependencies.CacheAs(_collectionService);
        _dependencies.CacheAs(_beatmapApiService);
        _dependencies.CacheAs(_scoreMigrationService);
        _dependencies.CacheAs(_trayIconService);
        _dependencies.CacheAs(_aptabaseService);
        _dependencies.CacheAs(_replayParserService);
        _dependencies.CacheAs(_timingDeviationCalculator);

        // Note: ScaledContentContainer will be cached after creation in load()
        return _dependencies;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        // Add embedded resources using AssemblyResourceStore which properly handles manifest names
        var assembly = typeof(OsuMappingHelperGame).Assembly;
        var assemblyStore = new AssemblyResourceStore(assembly, "OsuMappingHelper");
        Resources.AddStore(assemblyStore);

        // Load custom fonts from embedded resources
        AddFont(Resources, @"Resources/Fonts/Noto/Noto-Basic");
        
        // Create scaled content container for global UI scaling
        _scaledContainer = new ScaledContentContainer
        {
            ReferenceWidth = BaseWidth,
            ReferenceHeight = BaseHeight
        };

        // Add screen stack to scaled container
        _screenStack = new ScreenStack
        {
            RelativeSizeAxes = Axes.Both
        };

        _scaledContainer.Add(_screenStack);
        
        Add(_scaledContainer);
        
        // Add results overlay for timing deviation display OUTSIDE the scaled container
        // This allows it to properly fill the replay analysis window (800x400)
        _resultsOverlay = new ResultsOverlayWindow
        {
            RelativeSizeAxes = Axes.Both,
            Depth = float.MinValue, // Ensure it's on top
            Alpha = 0 // Start hidden
        };
        _resultsOverlay.CloseRequested += (_, _) => ExitReplayAnalysisMode();
        _resultsOverlay.ReanalysisRequested += HandleReanalysisRequest;
        Add(_resultsOverlay);

        // Cache the scaled container so other components can access it
        _dependencies.CacheAs(_scaledContainer);

        // Push appropriate screen based on mode
        // Native splash screen handles the startup animation
        if (_trainingMode)
        {
            _trainingScreen = new TrainingScreen();
            _screenStack.Push(_trainingScreen);
        }
        else
        {
            _mainScreen = new MainScreen();
            _screenStack.Push(_mainScreen);
        }
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        Window.Resizable = false;

        // Initialize services
        Task.Run(async () =>
        {
            await _userSettingsService.InitializeAsync();
            await _danConfigService.InitializeAsync();
            
            // Apply analytics setting from user preferences (GDPR compliance)
            _aptabaseService.IsEnabled = _userSettingsService.Settings.SendAnalytics;
            
            // Apply MinaCalc version setting
            ToolPaths.SelectedMinaCalcVersion = _userSettingsService.Settings.MinaCalcVersion;
            
            // Track app startup (only if analytics is enabled)
            _aptabaseService.TrackAppStarted(_trainingMode);
            
            // Restore window settings on UI thread
            Schedule(() =>
            {
                RestoreWindowSettings();
                RestoreUIScale();
                InitializeOverlayAndHotkeys();
                MakeWindowBorderless();
                InitializeTrayIcon();
                
                // Auto-start session if enabled in settings
                if (_userSettingsService.Settings.AutoStartSession && !_sessionTrackerService.IsTracking)
                {
                    _sessionTrackerService.StartSession();
                    Console.WriteLine("[Session] Auto-started session on startup");
                }
                
                // Close the native splash screen now that the game is ready
                _closeSplashScreen?.Invoke();
                
                // Bring game window to foreground after splash closes
                BringWindowToFocus();
            });
        });

        // Set window properties after load is complete
        if (Window != null)
        {
            Window.Title = _trainingMode ? "Companella! - Training Mode" : "Companella!";
            
            // Configure osu!-style borderless window
            Window.CursorState = osu.Framework.Platform.CursorState.Default;
            
            // Subscribe to file drop events
            Window.DragDrop += OnWindowFileDrop;
            
            // Subscribe to window state changes to save settings
            Window.WindowStateChanged += OnWindowStateChanged;
            
            // Try to restore window settings immediately if available
            if (_userSettingsService != null)
            {
                RestoreWindowSettings();
            }
            
            // Enforce window size immediately
            Schedule(() =>
            {
                var windowTitle = Window.Title;
                var handle = WindowHandleHelper.GetCurrentProcessWindowHandle(windowTitle);
                if (handle != IntPtr.Zero)
                {
                    var currentX = Window.Position.X;
                    var currentY = Window.Position.Y;
                    SetWindowPos(handle, IntPtr.Zero, currentX, currentY, _currentTargetWidth, _currentTargetHeight, 
                        SWP_NOZORDER | SWP_FRAMECHANGED);
                }
            });
        }
    }

    private const int BaseWidth = 620;
    private const int BaseHeight = 810;
    
    // Current scaled window dimensions
    private int _currentTargetWidth = BaseWidth;
    private int _currentTargetHeight = BaseHeight;

    private void ForceWindowResolution(int width, int height)
    {
        if (Window == null) return;

        // Use only osu!framework API to set window properties
        // Note: Window.Size is read-only, so we monitor and correct size changes
        Window.WindowState = osu.Framework.Platform.WindowState.Normal;
        
        // Store target size for monitoring
        _targetWindowSize = new System.Drawing.Size(width, height);
    }

    /// <summary>
    /// Performs a one-time restart of osu! after first connection to ensure proper process attachment.
    /// This only happens once per app session.
    /// </summary>
    private void PerformFirstConnectionRestart()
    {
        if (_hasPerformedStartupRestart) return;
        
        _hasPerformedStartupRestart = true;
        
        try
        {
            Console.WriteLine("[Startup] First connection detected, performing quick restart for proper attachment...");
            _collectionService.RestartOsu();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Startup] Error during osu! restart: {ex.Message}");
        }
    }

    private void RestoreUIScale()
    {
        if (_userSettingsService == null || _scaledContainer == null) return;
        
        var savedScale = _userSettingsService.Settings.UIScale;
        
        // Clamp to valid range
        savedScale = Math.Clamp(savedScale, 0.5f, 2.0f);
        
        _scaledContainer.UIScale = savedScale;
        
        // Apply window size based on scale
        ApplyWindowScale(savedScale);
        
        // Subscribe to future scale changes
        _scaledContainer.UIScaleBindable.BindValueChanged(e =>
        {
            // Must schedule to ensure we're on the correct thread for Windows API calls
            Schedule(() => ApplyWindowScale(e.NewValue));
        });
        
        Console.WriteLine($"[UIScale] Restored UI scale: {savedScale:P0}");
    }
    
    /// <summary>
    /// Applies window size based on the UI scale factor.
    /// </summary>
    private void ApplyWindowScale(float scale)
    {
        if (Window == null) return;
        
        // Calculate new window dimensions
        _currentTargetWidth = (int)(BaseWidth * scale);
        _currentTargetHeight = (int)(BaseHeight * scale);
        
        // Apply via Windows API
        var windowTitle = Window.Title;
        var handle = WindowHandleHelper.GetCurrentProcessWindowHandle(windowTitle);
        
        if (handle != IntPtr.Zero)
        {
            // Ensure borderless style is set
            var borderlessStyle = WS_POPUP | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN;
            SetWindowLong(handle, GWL_STYLE, borderlessStyle);
            
            var currentX = Window.Position.X;
            var currentY = Window.Position.Y;
            
            // Apply size change with frame update
            SetWindowPos(handle, IntPtr.Zero, currentX, currentY, _currentTargetWidth, _currentTargetHeight, 
                SWP_NOZORDER | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
            
            Console.WriteLine($"[UIScale] Window resized to {_currentTargetWidth}x{_currentTargetHeight} (scale: {scale:P0})");
        }
        else
        {
            Console.WriteLine($"[UIScale] Failed to get window handle for resize");
        }
    }
    
    /// <summary>
    /// Block Alt+Enter to prevent fullscreen toggle.
    /// </summary>
    protected override bool OnKeyDown(KeyDownEvent e)
    {
        // Block Alt+Enter (fullscreen toggle)
        if (e.Key == Key.Enter && e.AltPressed)
        {
            return true; // Consume the event
        }
        
        return base.OnKeyDown(e);
    }

    private void RestoreWindowSettings()
    {
        if (Window == null || _userSettingsService == null) return;

        var settings = _userSettingsService.Settings;
        
        // Restore window state first (this might affect size/position)
        if (Enum.TryParse<osu.Framework.Platform.WindowState>(settings.WindowState, out var windowState))
        {
            Window.WindowState = windowState;
        }
        else
        {
            Window.WindowState = osu.Framework.Platform.WindowState.Normal;
        }
        
        // Note: Window.Size and Window.Position are read-only in osu!framework
        // Window size/position restoration would need to be done through the host/window implementation
        // For now, we'll only restore window state and save the current size/position
        
        // Initialize tracking variables
        _lastWindowSize = Window.Size;
        _lastWindowPosition = Window.Position;
    }

    private void OnWindowStateChanged(WindowState newState)
    {
        SaveWindowSettings();
    }

    private void SaveWindowSettings()
    {
        if (Window == null || _userSettingsService == null) return;

        var settings = _userSettingsService.Settings;
        
        // Save window size and position
        settings.WindowWidth = Window.Size.Width;
        settings.WindowHeight = Window.Size.Height;
        settings.WindowX = Window.Position.X;
        settings.WindowY = Window.Position.Y;
        settings.WindowState = Window.WindowState.ToString();
        
        // Update tracking variables
        _lastWindowSize = Window.Size;
        _lastWindowPosition = Window.Position;
        
        // Save asynchronously
        Task.Run(async () => await _userSettingsService.SaveAsync());
    }

    /// <summary>
    /// Initializes overlay and hotkey services.
    /// </summary>
    private void InitializeOverlayAndHotkeys()
    {
        if (_userSettingsService == null || _overlayService == null || _hotkeyService == null)
            return;

        var settings = _userSettingsService.Settings;

        // Initialize overlay mode (will be auto-enabled when osu! is detected)
        _overlayService.OsuWindowChanged += OnOsuWindowChanged;
        
        // Apply saved overlay offset
        _overlayService.OverlayOffset = new System.Drawing.Point(
            settings.OverlayOffsetX,
            settings.OverlayOffsetY
        );
        
        // Store initial window position
        if (Window != null)
        {
            _savedWindowPosition = Window.Position;
        }

        // Initialize hotkey (will need window handle - see note below)
        // Note: osu!Framework doesn't expose window handle directly
        // We'll need to use a different approach or get handle via reflection/Windows API
        InitializeHotkey(settings.ToggleVisibilityKeybind);

        // Subscribe to hotkey press
        _hotkeyService.HotkeyPressed += OnToggleVisibilityHotkeyPressed;
    }

    /// <summary>
    /// Initializes the global hotkey using a message-only window.
    /// </summary>
    private void InitializeHotkey(string keybind)
    {
        if (_hotkeyService == null)
            return;

        try
        {
            // Initialize with IntPtr.Zero to create a message-only window
            // This is more reliable than trying to find the osu!Framework window handle
            _hotkeyService.Initialize(IntPtr.Zero);
            
            if (_hotkeyService.RegisterHotkey(keybind))
            {
                Console.WriteLine($"[Hotkey] Registered hotkey: {keybind}");
            }
            else
            {
                Console.WriteLine("[Hotkey] Failed to register hotkey - it may already be in use");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Hotkey] Error initializing hotkey: {ex.Message}");
        }
    }

    /// <summary>
    /// Initializes the system tray icon.
    /// </summary>
    private void InitializeTrayIcon()
    {
        if (_trayIconService == null)
            return;

        try
        {
            _trayIconService.Initialize();
            _trayIconService.CheckForUpdatesRequested += OnTrayCheckForUpdatesRequested;
            _trayIconService.ExitRequested += OnTrayExitRequested;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TrayIcon] Error initializing tray icon: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles the tray icon "Check for Updates" menu item click.
    /// </summary>
    private async void OnTrayCheckForUpdatesRequested(object? sender, EventArgs e)
    {
        if (_autoUpdaterService == null)
            return;

        try
        {
            _trayIconService?.ShowNotification("Companella!", "Checking for updates...", System.Windows.Forms.ToolTipIcon.Info, 2000);
            
            var updateInfo = await _autoUpdaterService.CheckForUpdatesAsync();
            
            if (updateInfo != null)
            {
                _trayIconService?.ShowNotification(
                    "Update Available", 
                    $"Version {updateInfo.TagName} is available. Open the app to update.",
                    System.Windows.Forms.ToolTipIcon.Info,
                    5000);
            }
            else
            {
                _trayIconService?.ShowNotification(
                    "Companella!", 
                    $"You are running the latest version ({_autoUpdaterService.CurrentVersion}).",
                    System.Windows.Forms.ToolTipIcon.Info,
                    3000);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TrayIcon] Error checking for updates: {ex.Message}");
            _trayIconService?.ShowNotification(
                "Update Check Failed", 
                "Could not check for updates. Please try again later.",
                System.Windows.Forms.ToolTipIcon.Warning,
                3000);
        }
    }

    /// <summary>
    /// Handles the tray icon "Exit" menu item click.
    /// </summary>
    private void OnTrayExitRequested(object? sender, EventArgs e)
    {
        // Exit the application gracefully
        Schedule(() =>
        {
            Host.Exit();
        });
    }

    /// <summary>
    /// Handles the play ended with pauses event.
    /// Shows a notification with the pause count.
    /// </summary>
    private void OnPlayEndedWithPauses(object? sender, int pauseCount)
    {
        var message = pauseCount == 1 
            ? "You paused 1 time >:c" 
            : $"You paused {pauseCount} times >:c";
        
        _trayIconService?.ShowNotification("Pause Counter", message, System.Windows.Forms.ToolTipIcon.Warning, 3000);
    }

    /// <summary>
    /// Handles entering the results screen after completing a map or viewing a replay.
    /// Triggers timing deviation analysis by reading hit errors from memory.
    /// </summary>
    private void OnResultsScreenEntered(object? sender, ResultsScreenEventArgs e)
    {
        // Check if replay analysis is enabled
        if (!_userSettingsService.Settings.ReplayAnalysisEnabled)
        {
            Console.WriteLine("[TimingDeviation] Replay analysis is disabled in settings");
            return;
        }
        
        var source = e.IsReplayView ? "viewing replay" : "completed play";
        Console.WriteLine($"[TimingDeviation] Results screen entered ({source}): {Path.GetFileName(e.BeatmapPath)}");
        
        // Schedule on UI thread
        Schedule(() =>
        {
            if (_resultsOverlay == null) return;
            
            // Switch to replay analysis window mode
            EnterReplayAnalysisMode();
            
            _resultsOverlay.ShowLoading();
            
            // Run analysis in background
            Task.Run(() =>
            {
                try
                {
                    // Small delay to ensure osu! has updated memory with results
                    Thread.Sleep(300);
                    
                    // Read hit errors directly from memory
                    // This works for both fresh plays and replay viewing - osu! loads the data into memory
                    Console.WriteLine("[TimingDeviation] Reading hit errors from osu! memory...");
                    var result = _hitErrorReaderService.ReadHitErrorsWithBeatmap(e.BeatmapPath, _fileParser, e.Rate);
                    
                    if (result != null && result.Success && result.Deviations.Count > 0)
                    {
                        Console.WriteLine($"[TimingDeviation] Memory read successful: UR={result.UnstableRate:F2}, Mean={result.MeanDeviation:F2}ms, Hits={result.Deviations.Count}");
                        Schedule(() => _resultsOverlay?.ShowData(result));
                        return;
                    }
                    
                    // If viewing a replay/score, try to find the exact replay using scores.db
                    if (e.IsReplayView)
                    {
                        Console.WriteLine("[TimingDeviation] Memory read failed for replay view - trying to identify specific replay...");
                        
                        // Step 1: Read score data from results screen
                        var resultsData = _hitErrorReaderService.TryReadResultsScreenData();
                        if (resultsData != null)
                        {
                            Console.WriteLine($"[TimingDeviation] Results screen data: {resultsData}");
                            
                            // Step 2: Get beatmap hash
                            var beatmapHash = _replayParserService.GetBeatmapHash(e.BeatmapPath);
                            if (!string.IsNullOrEmpty(beatmapHash))
                            {
                                Console.WriteLine($"[TimingDeviation] Beatmap hash: {beatmapHash}");
                                
                                // Step 3: Find matching replay using scores.db
                                var matchingReplay = _replayParserService.FindReplayByScoreData(resultsData, beatmapHash);
                                
                                if (matchingReplay != null)
                                {
                                    // Get key count from beatmap
                                    var replayOsuFile = _fileParser.Parse(e.BeatmapPath);
                                    int replayKeyCount = (int)replayOsuFile.CircleSize;
                                    
                                    // Extract key events from replay
                                    var replayKeyEvents = _replayParserService.ExtractManiaKeyEvents(matchingReplay, replayKeyCount);
                                    
                                    if (replayKeyEvents.Count > 0)
                                    {
                                        // Calculate timing deviations
                                        var replayRate = _replayParserService.GetRateFromMods(matchingReplay);
                                        var hasMirror = _replayParserService.HasMirrorMod(matchingReplay);
                                        Console.WriteLine($"[TimingDeviation] Replay mods: {matchingReplay.Mods}, rate: {replayRate}x, mirror: {hasMirror}");
                                        var matchedAnalysis = _timingDeviationCalculator.CalculateDeviations(e.BeatmapPath, replayKeyEvents, replayRate, hasMirror);
                                        
                                        if (matchedAnalysis.Success)
                                        {
                                            Console.WriteLine($"[TimingDeviation] Exact replay analysis complete: UR={matchedAnalysis.UnstableRate:F2}, Mean={matchedAnalysis.MeanDeviation:F2}ms");
                                            Schedule(() => _resultsOverlay?.ShowData(matchedAnalysis));
                                            return;
                                        }
                                    }
                                }
                            }
                        }
                        
                        Console.WriteLine("[TimingDeviation] Could not identify the specific replay for this score");
                        Schedule(() => _resultsOverlay?.ShowError("Could not find replay data for this score"));
                        return;
                    }
                    
                    // For fresh plays only: Try to find replay file as fallback
                    Console.WriteLine("[TimingDeviation] Memory read failed, trying replay file fallback...");
                    Thread.Sleep(1000);
                    var replayResult = _replayParserService.FindAndParseRecentReplay(120);
                    
                    if (replayResult is not { } replay)
                    {
                        Console.WriteLine("[TimingDeviation] No replay file found");
                        Schedule(() => _resultsOverlay?.ShowError("Could not read hit error data"));
                        return;
                    }
                    
                    // Get key count from beatmap
                    var osuFile = _fileParser.Parse(e.BeatmapPath);
                    int keyCount = (int)osuFile.CircleSize;
                    
                    // Extract key events from replay
                    var keyEvents = _replayParserService.ExtractManiaKeyEvents(replay, keyCount);
                    
                    if (keyEvents.Count == 0)
                    {
                        Console.WriteLine("[TimingDeviation] No key events in replay");
                        Schedule(() => _resultsOverlay?.ShowError("No timing data available"));
                        return;
                    }
                    
                    // Calculate timing deviations
                    var rate = _replayParserService.GetRateFromMods(replay);
                    var mirror = _replayParserService.HasMirrorMod(replay);
                    Console.WriteLine($"[TimingDeviation] Replay rate: {rate}x, mirror: {mirror}");
                    var replayAnalysis = _timingDeviationCalculator.CalculateDeviations(e.BeatmapPath, keyEvents, rate, mirror);
                    
                    if (replayAnalysis.Success)
                    {
                        Console.WriteLine($"[TimingDeviation] Replay file analysis complete: UR={replayAnalysis.UnstableRate:F2}, Mean={replayAnalysis.MeanDeviation:F2}ms");
                        Schedule(() => _resultsOverlay?.ShowData(replayAnalysis));
                    }
                    else
                    {
                        Console.WriteLine($"[TimingDeviation] Analysis failed: {replayAnalysis.ErrorMessage}");
                        Schedule(() => _resultsOverlay?.ShowError(replayAnalysis.ErrorMessage ?? "Analysis failed"));
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TimingDeviation] Error during analysis: {ex.Message}");
                    Schedule(() => _resultsOverlay?.ShowError($"Error: {ex.Message}"));
                }
            });
        });
    }

    /// <summary>
    /// Handles leaving the results screen.
    /// Hides the timing deviation overlay and restores window state.
    /// </summary>
    private void OnResultsScreenExited(object? sender, EventArgs e)
    {
        Console.WriteLine("[TimingDeviation] Results screen exited");
        Schedule(() =>
        {
            _resultsOverlay?.Hide();
            ExitReplayAnalysisMode();
        });
    }
    
    /// <summary>
    /// Handles a request to re-analyze with a different OD.
    /// Runs a full re-analysis with the new OD value.
    /// </summary>
    private void HandleReanalysisRequest(double newOD)
    {
        if (_resultsOverlay?.CurrentData == null)
        {
            Console.WriteLine("[TimingDeviation] Re-analysis requested but no current data");
            return;
        }
        
        var currentData = _resultsOverlay.CurrentData;
        
        // Check if we have the original key events for re-analysis
        if (currentData.OriginalKeyEvents == null || currentData.OriginalKeyEvents.Count == 0)
        {
            Console.WriteLine("[TimingDeviation] Re-analysis requested but no original key events stored");
            return;
        }
        
        Console.WriteLine($"[TimingDeviation] Re-analysis requested with OD={newOD}");
        
        // Run full re-analysis in background
        Task.Run(() =>
        {
            try
            {
                var newAnalysis = _timingDeviationCalculator.CalculateDeviations(
                    currentData.BeatmapPath,
                    currentData.OriginalKeyEvents,
                    currentData.Rate,
                    currentData.HasMirror,
                    newOD // Custom OD
                );
                
                if (newAnalysis.Success)
                {
                    Console.WriteLine($"[TimingDeviation] Re-analysis complete: UR={newAnalysis.UnstableRate:F2}, Mean={newAnalysis.MeanDeviation:F2}ms");
                    Schedule(() => _resultsOverlay?.ShowData(newAnalysis));
                }
                else
                {
                    Console.WriteLine($"[TimingDeviation] Re-analysis failed: {newAnalysis.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TimingDeviation] Re-analysis error: {ex.Message}");
            }
        });
    }
    
    /// <summary>
    /// Enters replay analysis mode - resizes window to 8:4 aspect ratio.
    /// </summary>
    private void EnterReplayAnalysisMode()
    {
        if (_isInReplayAnalysisMode) return;
        
        Console.WriteLine("[ReplayAnalysis] Entering replay analysis mode");
        
        // Save current window state
        _savedWindowSizeBeforeAnalysis = Window.Size;
        _savedWindowPositionBeforeAnalysis = Window.Position;
        
        // Hide main app content, show only replay analysis
        _scaledContainer.FadeTo(0, 100);
        
        // Get settings for replay analysis window
        var settings = _userSettingsService.Settings;
        var scale = settings.UIScale;
        var width = (int)(settings.ReplayAnalysisWidth * scale);
        var height = (int)(settings.ReplayAnalysisHeight * scale);
        var x = settings.ReplayAnalysisX;
        var y = settings.ReplayAnalysisY;
        
        Console.WriteLine($"[ReplayAnalysis] Resizing window to {width}x{height} at ({x}, {y})");
        
        // Use SetWindowPos to resize and reposition window
        var windowTitle = Window.Title;
        var handle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
        if (handle == IntPtr.Zero)
        {
            handle = FindWindow(null, windowTitle);
        }
        
        if (handle != IntPtr.Zero)
        {
            SetWindowPos(handle, IntPtr.Zero, x, y, width, height, SWP_NOZORDER | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
        }
        
        _isInReplayAnalysisMode = true;
    }
    
    /// <summary>
    /// Exits replay analysis mode - restores original window size and position.
    /// </summary>
    private void ExitReplayAnalysisMode()
    {
        if (!_isInReplayAnalysisMode) return;
        
        Console.WriteLine("[ReplayAnalysis] Exiting replay analysis mode");
        
        // Restore original window state
        Console.WriteLine($"[ReplayAnalysis] Restoring window to {_savedWindowSizeBeforeAnalysis.Width}x{_savedWindowSizeBeforeAnalysis.Height} at ({_savedWindowPositionBeforeAnalysis.X}, {_savedWindowPositionBeforeAnalysis.Y})");
        
        var windowTitle = Window.Title;
        var handle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
        if (handle == IntPtr.Zero)
        {
            handle = FindWindow(null, windowTitle);
        }
        
        if (handle != IntPtr.Zero)
        {
            SetWindowPos(handle, IntPtr.Zero, 
                _savedWindowPositionBeforeAnalysis.X, _savedWindowPositionBeforeAnalysis.Y, 
                _savedWindowSizeBeforeAnalysis.Width, _savedWindowSizeBeforeAnalysis.Height, 
                SWP_NOZORDER | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
        }
        
        // Show main app content again
        _scaledContainer.FadeTo(1, 100);
        
        _isInReplayAnalysisMode = false;
    }

    /// <summary>
    /// Handles osu! window position changes for overlay mode.
    /// </summary>
    private void OnOsuWindowChanged(object? sender, System.Drawing.Rectangle osuRect)
    {
        if (!_overlayService.IsOverlayMode || Window == null)
            return;

        UpdateOverlayPosition();
    }

    /// <summary>
    /// Updates the overlay window position relative to osu! window.
    /// Only shows overlay if osu! or the overlay itself is in focus and user hasn't hidden it.
    /// </summary>
    private void UpdateOverlayPosition()
    {
        // Skip if in replay analysis mode - don't override its window position
        if (_isInReplayAnalysisMode)
            return;
        
        if (!_overlayService.IsOverlayMode || Window == null)
            return;

        // Respect user's manual hide via hotkey
        if (!_isWindowVisible)
            return;

        var windowTitle = Window.Title;
        var handle = WindowHandleHelper.GetCurrentProcessWindowHandle(windowTitle);
        
        if (handle == IntPtr.Zero)
            return;

        // Only show overlay if osu! or the overlay window is in focus
        // This prevents the overlay from hiding when clicked
        if (!_overlayService.IsOsuOrOverlayInFocus(handle))
        {
            // Hide overlay window when neither osu! nor overlay is in focus
            HideOverlayWindow();
            return;
        }

        var overlayPos = _overlayService.CalculateOverlayPosition(_currentTargetWidth, _currentTargetHeight);
        if (overlayPos.HasValue)
        {
            // Set window to topmost and position it, enforcing size
            // Include SWP_FRAMECHANGED to ensure transparency is applied
            SetWindowPos(handle, HWND_TOPMOST, overlayPos.Value.X, overlayPos.Value.Y, _currentTargetWidth, _currentTargetHeight, 
                SWP_SHOWWINDOW | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }
    }

    /// <summary>
    /// Hides the overlay window when osu! is not in focus.
    /// </summary>
    private void HideOverlayWindow()
    {
        if (Window == null)
            return;

        try
        {
            var windowTitle = Window.Title;
            var handle = WindowHandleHelper.GetCurrentProcessWindowHandle(windowTitle);
            
            if (handle != IntPtr.Zero)
            {
                // Hide window but keep it topmost (so it appears instantly when osu! regains focus)
                const int SW_HIDE = 0;
                ShowWindow(handle, SW_HIDE);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Overlay] Error hiding overlay window: {ex.Message}");
        }
    }

    /// <summary>
    /// Makes the window borderless (removes title bar) and enforces size - always applied.
    /// </summary>
    private void MakeWindowBorderless()
    {
        if (Window == null)
            return;

        var windowTitle = Window.Title;
        var handle = WindowHandleHelper.GetCurrentProcessWindowHandle(windowTitle);
        
        if (handle != IntPtr.Zero)
        {
            // Set borderless window style (no title bar, no border)
            var borderlessStyle = WS_POPUP | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN;
            SetWindowLong(handle, GWL_STYLE, borderlessStyle);
            
            // Get current window position
            var currentX = Window.Position.X;
            var currentY = Window.Position.Y;
            
            // Apply style changes and enforce window size
            SetWindowPos(handle, IntPtr.Zero, currentX, currentY, _currentTargetWidth, _currentTargetHeight,
                SWP_NOZORDER | SWP_FRAMECHANGED);
        }
    }

    /// <summary>
    /// Brings the game window to the foreground and gives it focus.
    /// </summary>
    private void BringWindowToFocus()
    {
        if (Window == null)
            return;

        try
        {
            var windowTitle = Window.Title;
            var handle = WindowHandleHelper.GetCurrentProcessWindowHandle(windowTitle);

            if (handle != IntPtr.Zero)
            {
                // Show window and bring to foreground
                const int SW_SHOW = 5;
                ShowWindow(handle, SW_SHOW);
                SetForegroundWindow(handle);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Focus] Error bringing window to focus: {ex.Message}");
        }
    }

    /// <summary>
    /// Enables overlay mode and positions window relative to osu!.
    /// </summary>
    private void EnableOverlayMode()
    {
        if (_overlayService.IsOverlayMode || Window == null)
            return;

        // Save current window position
        _savedWindowPosition = Window.Position;

        // Enable window transparency FIRST, before making borderless
        // This ensures the layered window style is set correctly
        var windowTitle = Window.Title;
        var handle = WindowHandleHelper.GetCurrentProcessWindowHandle(windowTitle);

        // Ensure window is borderless (should already be, but make sure)
        MakeWindowBorderless();
        
        // Force a final refresh to ensure everything is applied
        if (handle != IntPtr.Zero)
        {
            SetWindowPos(handle, IntPtr.Zero, Window.Position.X, Window.Position.Y, 
                Window.Size.Width, Window.Size.Height, 
                SWP_NOZORDER | SWP_FRAMECHANGED);
        }

        // Enable overlay mode
        _overlayService.IsOverlayMode = true;
        
        // Update position immediately
        UpdateOverlayPosition();
        
        Console.WriteLine("[Overlay] Overlay mode enabled - window following osu! (transparent)");
    }

    /// <summary>
    /// Disables overlay mode and restores window to saved position.
    /// Window remains borderless (no title bar).
    /// </summary>
    private void DisableOverlayMode()
    {
        if (!_overlayService.IsOverlayMode)
            return;

        // Disable overlay mode
        _overlayService.IsOverlayMode = false;
        
        // Restore saved window position and remove topmost flag
        // Window remains borderless (no title bar)
        if (Window != null)
        {
            var windowTitle = Window.Title;
            var handle = WindowHandleHelper.GetCurrentProcessWindowHandle(windowTitle);
            
            if (handle != IntPtr.Zero)
            {
                // Ensure window is still borderless
                MakeWindowBorderless();
                
                // Remove topmost flag and restore position, enforcing size
                SetWindowPos(handle, HWND_NOTOPMOST, _savedWindowPosition.X, _savedWindowPosition.Y, _currentTargetWidth, _currentTargetHeight, 
                    SWP_SHOWWINDOW | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            }
        }
        
        Console.WriteLine("[Overlay] Overlay mode disabled - window restored to saved position (borderless)");
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLongPtr(IntPtr hWnd, int nIndex, int dwNewLong);

    
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    
    private const int GWL_STYLE = -16;
    private const int WS_OVERLAPPEDWINDOW = unchecked((int)0x00CF0000);
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_VISIBLE = unchecked((int)0x10000000);
    private const int WS_CLIPSIBLINGS = unchecked((int)0x04000000);
    private const int WS_CLIPCHILDREN = unchecked((int)0x02000000);
    
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_FRAMECHANGED = 0x0020;
    

    /// <summary>
    /// Handles the toggle visibility hotkey press.
    /// Only toggles visibility when in overlay mode.
    /// </summary>
    private void OnToggleVisibilityHotkeyPressed(object? sender, EventArgs e)
    {
        // Only toggle visibility when in overlay mode
        if (_overlayService?.IsOverlayMode == true)
        {
            // Enforce cooldown to prevent rapid toggling
            var timeSinceLastToggle = (DateTimeOffset.Now - _lastOverlayToggleTime).TotalMilliseconds;
            if (timeSinceLastToggle < OverlayToggleCooldownMs)
            {
                return;
            }
            
            _lastOverlayToggleTime = DateTimeOffset.Now;
            ToggleWindowVisibility();
        }
    }

    /// <summary>
    /// Toggles window visibility using Windows API.
    /// </summary>
    public void ToggleWindowVisibility()
    {
        if (Window == null)
            return;

        try
        {
            var windowTitle = Window.Title;
            var handle = WindowHandleHelper.GetCurrentProcessWindowHandle(windowTitle);
            
            if (handle != IntPtr.Zero)
            {
                _isWindowVisible = !_isWindowVisible;
                
                // Use Windows API to show/hide window
                const int SW_HIDE = 0;
                const int SW_SHOW = 5;
                
                ShowWindow(handle, _isWindowVisible ? SW_SHOW : SW_HIDE);
                Console.WriteLine($"[Overlay] Overlay visibility toggled via hotkey: {(_isWindowVisible ? "Visible" : "Hidden")}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Overlay] Error toggling window visibility: {ex.Message}");
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    protected override void Update()
    {
        base.Update();

        // Check osu! process status and auto-enable/disable overlay mode
        CheckOsuProcessStatus();

        // Update overlay service
        _overlayService?.Update();

        // Update overlay position if in overlay mode
        if (_overlayService?.IsOverlayMode == true)
        {
            UpdateOverlayPosition();
        }

        // Periodically save window settings and prevent resizing using osu!framework API only
        if (Window != null)
        {
            _settingsSaveTimer += Clock.ElapsedFrameTime;
            if (_settingsSaveTimer >= SettingsSaveInterval)
            {
                _settingsSaveTimer = 0;
                
                // Prevent resizing by ensuring window state stays Normal (using osu!framework API)
                if (Window.WindowState != osu.Framework.Platform.WindowState.Normal)
                {
                    Window.WindowState = osu.Framework.Platform.WindowState.Normal;
                }
                
                // Enforce window size using Windows API (skip if in replay analysis mode)
                if (!_isInReplayAnalysisMode)
                {
                var currentSize = Window.Size;
                if (currentSize.Width != _currentTargetWidth || currentSize.Height != _currentTargetHeight)
                {
                        // Window was resized - force it back to target size
                    Window.WindowState = osu.Framework.Platform.WindowState.Normal;
                    
                    var windowTitle = Window.Title;
                    var handle = WindowHandleHelper.GetCurrentProcessWindowHandle(windowTitle);
                    if (handle != IntPtr.Zero)
                    {
                        var currentX = Window.Position.X;
                        var currentY = Window.Position.Y;
                        SetWindowPos(handle, IntPtr.Zero, currentX, currentY, _currentTargetWidth, _currentTargetHeight, 
                            SWP_NOZORDER | SWP_FRAMECHANGED);
                        }
                    }
                }
                
                // Only save window position if not in overlay mode
                if (!_overlayService?.IsOverlayMode == true)
                {
                    // Check if window size or position changed for saving
                    if (Window.Size.Width != _lastWindowSize.Width || 
                        Window.Size.Height != _lastWindowSize.Height ||
                        Window.Position.X != _lastWindowPosition.X ||
                        Window.Position.Y != _lastWindowPosition.Y)
                    {
                        SaveWindowSettings();
                    }
                }
            }
        }
    }

    /// <summary>
    /// Checks osu! process status and automatically enables/disables overlay mode.
    /// </summary>
    private void CheckOsuProcessStatus()
    {
        bool isOsuRunning = _processDetector.IsOsuRunning;

        // osu! just started
        if (isOsuRunning && !_wasOsuRunning)
        {
            // Try to attach to osu! process
            if (_processDetector.TryAttachToOsu())
            {
                var processInfo = _processDetector.GetProcessInfo();
                if (processInfo != null)
                {
                    try
                    {
                        var osuProcess = Process.GetProcessById(processInfo.ProcessId);
                        _overlayService.AttachToOsu(osuProcess);
                        
                        // Enable overlay mode automatically
                        EnableOverlayMode();
                        
                        Console.WriteLine("[Overlay] osu! detected - overlay mode enabled");
                        
                        // Perform one-time restart after first connection for proper attachment
                        if (!_hasPerformedStartupRestart)
                        {
                            PerformFirstConnectionRestart();
                            return; // Exit early, we'll re-detect after restart
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Overlay] Failed to attach to osu! process: {ex.Message}");
                    }
                }
            }
        }
        // osu! just closed
        else if (!isOsuRunning && _wasOsuRunning)
        {
            // Disable overlay mode and restore window position
            DisableOverlayMode();
            _overlayService.AttachToOsu(null);
            
            Console.WriteLine("[Overlay] osu! closed - overlay mode disabled");
        }
        // osu! is running but overlay service lost the process
        else if (isOsuRunning && _overlayService != null)
        {
            // Re-attach if needed
            var osuRect = _overlayService.GetOsuWindowRect();
            if (!osuRect.HasValue && _overlayService.IsOverlayMode)
            {
                // Process might have been lost, try to reattach
                if (_processDetector.TryAttachToOsu())
                {
                    var processInfo = _processDetector.GetProcessInfo();
                    if (processInfo != null)
                    {
                        try
                        {
                            var osuProcess = Process.GetProcessById(processInfo.ProcessId);
                            _overlayService.AttachToOsu(osuProcess);
                        }
                        catch
                        {
                            // Process might be closing, ignore
                        }
                    }
                }
            }
        }

        _wasOsuRunning = isOsuRunning;
    }

    private void OnWindowFileDrop(string file)
    {
        if (file.EndsWith(".osu", StringComparison.OrdinalIgnoreCase))
        {
            if (_trainingMode)
            {
                Schedule(() => _trainingScreen?.HandleFileDrop(file));
            }
            else
            {
                Schedule(() => _mainScreen?.HandleFileDrop(file));
            }
        }
    }

    protected override void Dispose(bool isDisposing)
    {
        if (Window != null)
        {
            Window.DragDrop -= OnWindowFileDrop;
            Window.WindowStateChanged -= OnWindowStateChanged;
            
            // Save settings before closing
            SaveWindowSettings();
        }
        
        // Auto-end session on exit if enabled in settings and session is active
        if (_userSettingsService?.Settings.AutoEndSession == true && _sessionTrackerService?.IsTracking == true)
        {
            _sessionTrackerService.StopSession();
            Console.WriteLine("[Session] Auto-ended session on exit");
        }
        
        // Unsubscribe from tray icon events
        if (_trayIconService != null)
        {
            _trayIconService.CheckForUpdatesRequested -= OnTrayCheckForUpdatesRequested;
            _trayIconService.ExitRequested -= OnTrayExitRequested;
        }
        
        // Unsubscribe from session tracker events
        if (_sessionTrackerService != null)
        {
            _sessionTrackerService.PlayEndedWithPauses -= OnPlayEndedWithPauses;
        }
        
        _processDetector?.Dispose();
        _overlayService?.Dispose();
        _hotkeyService?.Dispose();
        _autoUpdaterService?.Dispose();
        _sessionTrackerService?.Dispose();
        _sessionDatabaseService?.Dispose();
        _mapsDatabaseService?.Dispose();
        _trayIconService?.Dispose();
        _aptabaseService?.Dispose();
        base.Dispose(isDisposing);
    }
}

/// <summary>
/// Resource store that reads from assembly manifest resources with proper path conversion.
/// </summary>
public class AssemblyResourceStore : IResourceStore<byte[]>
{
    private readonly System.Reflection.Assembly _assembly;
    private readonly string _namespacePrefix;
    private readonly Dictionary<string, string> _pathToManifest = new();

    public AssemblyResourceStore(System.Reflection.Assembly assembly, string namespacePrefix)
    {
        _assembly = assembly;
        _namespacePrefix = namespacePrefix;

        // Build lookup from path-style names to manifest names
        foreach (var manifestName in assembly.GetManifestResourceNames())
        {
            if (manifestName.StartsWith(namespacePrefix + "."))
            {
                // Convert manifest name to path: OsuMappingHelper.Resources.Fonts.Noto.file.bin -> Resources/Fonts/Noto/file.bin
                string withoutPrefix = manifestName.Substring(namespacePrefix.Length + 1);
                string pathStyle = ConvertManifestToPath(withoutPrefix);
                _pathToManifest[pathStyle] = manifestName;
            }
        }
    }

    private static string ConvertManifestToPath(string manifestName)
    {
        // Find the last dot before the extension
        int lastDot = manifestName.LastIndexOf('.');
        if (lastDot <= 0) return manifestName.Replace('.', '/');

        // Everything before the extension uses dots for directories
        string pathPart = manifestName.Substring(0, lastDot);
        string extension = manifestName.Substring(lastDot);

        return pathPart.Replace('.', '/') + extension;
    }

    public byte[] Get(string name)
    {
        using var stream = GetStream(name);
        if (stream == null) return Array.Empty<byte>();

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public Task<byte[]> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Get(name));
    }

    public Stream? GetStream(string name)
    {
        // Try direct lookup
        if (_pathToManifest.TryGetValue(name, out string? manifestName))
        {
            return _assembly.GetManifestResourceStream(manifestName);
        }

        // Try with namespace prefix prepended
        string fullPath = _namespacePrefix + "/" + name;
        if (_pathToManifest.TryGetValue(fullPath, out manifestName))
        {
            return _assembly.GetManifestResourceStream(manifestName);
        }

        return null;
    }

    public IEnumerable<string> GetAvailableResources()
    {
        return _pathToManifest.Keys;
    }

    public void Dispose()
    {
    }
}
