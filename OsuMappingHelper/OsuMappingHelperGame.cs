using System.Diagnostics;
using System.Drawing;
using osu.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Platform;
using osu.Framework.Screens;
using OsuMappingHelper.Screens;
using OsuMappingHelper.Services;

namespace OsuMappingHelper;

/// <summary>
/// Main game class for the Companella! application.
/// </summary>
public partial class OsuMappingHelperGame : Game
{
    private ScreenStack _screenStack = null!;
    private DependencyContainer _dependencies = null!;
    private MainScreen? _mainScreen;
    private TrainingScreen? _trainingScreen;

    /// <summary>
    /// Whether the application is running in training mode.
    /// </summary>
    private readonly bool _trainingMode;

    // Settings saving
    private double _settingsSaveTimer;
    private const double SETTINGS_SAVE_INTERVAL = 2000; // Save every 2 seconds
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
    
    // Overlay state
    private bool _isWindowVisible = true;
    private bool _wasOsuRunning = false;
    private System.Drawing.Point _savedWindowPosition;

    /// <summary>
    /// Creates a new instance of the game.
    /// </summary>
    /// <param name="trainingMode">Whether to start in training mode.</param>
    public OsuMappingHelperGame(bool trainingMode = false)
    {
        _trainingMode = trainingMode;
    }

    protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
    {
        _dependencies = new DependencyContainer(base.CreateChildDependencies(parent));

        // Create and register services
        _processDetector = new OsuProcessDetector();
        _fileParser = new OsuFileParser();
        _fileWriter = new OsuFileWriter();
        _audioExtractor = new AudioExtractor();
        _timingConverter = new TimingPointConverter();
        _danConfigService = new DanConfigurationService();
        _trainingDataService = new TrainingDataService();
        _userSettingsService = new UserSettingsService();
        _overlayService = new OsuWindowOverlayService();
        _hotkeyService = new GlobalHotkeyService();

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

        return _dependencies;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        // Add screen stack
        _screenStack = new ScreenStack
        {
            RelativeSizeAxes = Axes.Both
        };

        Add(_screenStack);

        // Push appropriate screen based on mode
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
            
            // Restore window settings on UI thread
            Schedule(() =>
            {
                RestoreWindowSettings();
                InitializeOverlayAndHotkeys();
                MakeWindowBorderless();
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
                    SetWindowPos(handle, IntPtr.Zero, currentX, currentY, TARGET_WIDTH, TARGET_HEIGHT, 
                        SWP_NOZORDER | SWP_FRAMECHANGED);
                }
            });
        }
    }

    private const int TARGET_WIDTH = 620;
    private const int TARGET_HEIGHT = 810;

    private void ForceWindowResolution(int width, int height)
    {
        if (Window == null) return;

        // Use only osu!framework API to set window properties
        // Note: Window.Size is read-only, so we monitor and correct size changes
        Window.WindowState = osu.Framework.Platform.WindowState.Normal;
        
        // Store target size for monitoring
        _targetWindowSize = new System.Drawing.Size(width, height);
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

        var overlayPos = _overlayService.CalculateOverlayPosition(TARGET_WIDTH, TARGET_HEIGHT);
        if (overlayPos.HasValue)
        {
            // Set window to topmost and position it, enforcing size
            // Include SWP_FRAMECHANGED to ensure transparency is applied
            SetWindowPos(handle, HWND_TOPMOST, overlayPos.Value.X, overlayPos.Value.Y, TARGET_WIDTH, TARGET_HEIGHT, 
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
            SetWindowPos(handle, IntPtr.Zero, currentX, currentY, TARGET_WIDTH, TARGET_HEIGHT, 
                SWP_NOZORDER | SWP_FRAMECHANGED);
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
                SetWindowPos(handle, HWND_NOTOPMOST, _savedWindowPosition.X, _savedWindowPosition.Y, TARGET_WIDTH, TARGET_HEIGHT, 
                    SWP_SHOWWINDOW | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            }
        }
        
        Console.WriteLine("[Overlay] Overlay mode disabled - window restored to saved position (borderless)");
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

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
            if (_settingsSaveTimer >= SETTINGS_SAVE_INTERVAL)
            {
                _settingsSaveTimer = 0;
                
                // Prevent resizing by ensuring window state stays Normal (using osu!framework API)
                if (Window.WindowState != osu.Framework.Platform.WindowState.Normal)
                {
                    Window.WindowState = osu.Framework.Platform.WindowState.Normal;
                }
                
                // Enforce window size to always be 480x810 using Windows API
                var currentSize = Window.Size;
                if (currentSize.Width != TARGET_WIDTH || currentSize.Height != TARGET_HEIGHT)
                {
                    // Window was resized - force it back to 480x810
                    Window.WindowState = osu.Framework.Platform.WindowState.Normal;
                    
                    var windowTitle = Window.Title;
                    var handle = WindowHandleHelper.GetCurrentProcessWindowHandle(windowTitle);
                    if (handle != IntPtr.Zero)
                    {
                        var currentX = Window.Position.X;
                        var currentY = Window.Position.Y;
                        SetWindowPos(handle, IntPtr.Zero, currentX, currentY, TARGET_WIDTH, TARGET_HEIGHT, 
                            SWP_NOZORDER | SWP_FRAMECHANGED);
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
        _processDetector?.Dispose();
        _overlayService?.Dispose();
        _hotkeyService?.Dispose();
        base.Dispose(isDisposing);
    }
}
