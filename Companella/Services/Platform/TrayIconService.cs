using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Companella.Services.Common;

namespace Companella.Services.Platform;

/// <summary>
/// Service for managing the system tray icon and its context menu.
/// Runs on a dedicated STA thread with its own message loop.
/// </summary>
public class TrayIconService : IDisposable
{
    private NotifyIcon? _notifyIcon;
    private ContextMenuStrip? _contextMenu;
    private Thread? _trayThread;
    private bool _disposed;
    private readonly ManualResetEvent _initialized = new ManualResetEvent(false);
    private ApplicationContext? _appContext;

    /// <summary>
    /// Event raised when the user requests to check for updates.
    /// </summary>
    public event EventHandler? CheckForUpdatesRequested;

    /// <summary>
    /// Event raised when the user requests to exit the application.
    /// </summary>
    public event EventHandler? ExitRequested;

    /// <summary>
    /// Initializes the tray icon with the application icon and context menu.
    /// </summary>
    public void Initialize()
    {
        if (_trayThread != null)
            return;

        _trayThread = new Thread(TrayThreadProc)
        {
            Name = "TrayIconThread",
            IsBackground = true
        };
        _trayThread.SetApartmentState(ApartmentState.STA);
        _trayThread.Start();

        // Wait for initialization with timeout
        if (!_initialized.WaitOne(5000))
        {
            Logger.Info("[TrayIcon] Timeout waiting for tray icon initialization");
        }
    }

    /// <summary>
    /// The tray icon thread procedure - runs its own message loop.
    /// </summary>
    private void TrayThreadProc()
    {
        try
        {
            // Create context menu
            _contextMenu = new ContextMenuStrip();
            
            var checkUpdatesItem = new ToolStripMenuItem("Check for Updates");
            checkUpdatesItem.Click += OnCheckForUpdatesClick;
            _contextMenu.Items.Add(checkUpdatesItem);
            
            _contextMenu.Items.Add(new ToolStripSeparator());
            
            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += OnExitClick;
            _contextMenu.Items.Add(exitItem);

            // Create notify icon
            _notifyIcon = new NotifyIcon
            {
                Text = "Companella!",
                ContextMenuStrip = _contextMenu,
                Visible = true
            };

            // Load icon
            LoadIcon();

            Logger.Info("[TrayIcon] Tray icon initialized on dedicated thread");
            _initialized.Set();

            // Create application context and run message loop
            _appContext = new ApplicationContext();
            Application.Run(_appContext);
        }
        catch (Exception ex)
        {
            Logger.Info($"[TrayIcon] Exception in tray thread: {ex.Message}");
            _initialized.Set();
        }
        finally
        {
            CleanupTrayIcon();
        }
    }

    /// <summary>
    /// Cleans up tray icon resources on the tray thread.
    /// </summary>
    private void CleanupTrayIcon()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        if (_contextMenu != null)
        {
            _contextMenu.Dispose();
            _contextMenu = null;
        }
    }

    /// <summary>
    /// Loads the application icon for the tray.
    /// </summary>
    private void LoadIcon()
    {
        if (_notifyIcon == null)
            return;

        try
        {
            // Try to load from file first (same directory as executable)
            var exePath = Assembly.GetExecutingAssembly().Location;
            var exeDir = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory;
            
            // Try multiple locations
            var iconPaths = new[]
            {
                Path.Combine(exeDir, "icon.ico"),
                Path.Combine(exeDir, "..", "icon.ico"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico")
            };

            foreach (var iconPath in iconPaths)
            {
                if (File.Exists(iconPath))
                {
                    _notifyIcon.Icon = new Icon(iconPath);
                    Logger.Info($"[TrayIcon] Loaded icon from: {iconPath}");
                    return;
                }
            }

            // Try to extract from the application's main executable
            var mainExe = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(mainExe) && File.Exists(mainExe))
            {
                try
                {
                    var extractedIcon = Icon.ExtractAssociatedIcon(mainExe);
                    if (extractedIcon != null)
                    {
                        _notifyIcon.Icon = extractedIcon;
                        Logger.Info($"[TrayIcon] Extracted icon from executable: {mainExe}");
                        return;
                    }
                }
                catch
                {
                    // Fall through to default
                }
            }

            // Fall back to system application icon
            _notifyIcon.Icon = SystemIcons.Application;
            Logger.Info("[TrayIcon] Using default system icon");
        }
        catch (Exception ex)
        {
            Logger.Info($"[TrayIcon] Failed to load icon: {ex.Message}");
            try
            {
                _notifyIcon.Icon = SystemIcons.Application;
            }
            catch
            {
                // Icon loading completely failed
            }
        }
    }

    /// <summary>
    /// Shows a balloon notification in the system tray.
    /// Thread-safe - can be called from any thread.
    /// </summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification message.</param>
    /// <param name="icon">The icon to display.</param>
    /// <param name="timeout">The timeout in milliseconds.</param>
    public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info, int timeout = 3000)
    {
        if (_notifyIcon == null || _contextMenu == null)
            return;

        try
        {
            // Use Invoke to marshal to the tray thread
            if (_contextMenu.InvokeRequired)
            {
                _contextMenu.BeginInvoke(new Action(() =>
                {
                    _notifyIcon?.ShowBalloonTip(timeout, title, message, icon);
                }));
            }
            else
            {
                _notifyIcon.ShowBalloonTip(timeout, title, message, icon);
            }
        }
        catch (Exception ex)
        {
            Logger.Info($"[TrayIcon] Error showing notification: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates the tooltip text for the tray icon.
    /// Thread-safe - can be called from any thread.
    /// </summary>
    /// <param name="text">The new tooltip text.</param>
    public void SetTooltip(string text)
    {
        if (_notifyIcon == null || _contextMenu == null)
            return;

        try
        {
            // Tooltip text is limited to 63 characters
            var truncatedText = text.Length > 63 ? text.Substring(0, 63) : text;
            
            if (_contextMenu.InvokeRequired)
            {
                _contextMenu.BeginInvoke(new Action(() =>
                {
                    if (_notifyIcon != null)
                        _notifyIcon.Text = truncatedText;
                }));
            }
            else
            {
                _notifyIcon.Text = truncatedText;
            }
        }
        catch (Exception ex)
        {
            Logger.Info($"[TrayIcon] Error setting tooltip: {ex.Message}");
        }
    }

    private void OnCheckForUpdatesClick(object? sender, EventArgs e)
    {
        Logger.Info("[TrayIcon] Check for updates requested");
        // Fire event on a thread pool thread to not block the UI thread
        Task.Run(() => CheckForUpdatesRequested?.Invoke(this, EventArgs.Empty));
    }

    private void OnExitClick(object? sender, EventArgs e)
    {
        Logger.Info("[TrayIcon] Exit requested");
        // Fire event on a thread pool thread to not block the UI thread
        Task.Run(() => ExitRequested?.Invoke(this, EventArgs.Empty));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Exit the application context's message loop
        if (_appContext != null)
        {
            try
            {
                if (_contextMenu != null && _contextMenu.InvokeRequired)
                {
                    _contextMenu.BeginInvoke(new Action(() =>
                    {
                        _appContext?.ExitThread();
                    }));
                }
                else
                {
                    _appContext.ExitThread();
                }
            }
            catch
            {
                // Context may already be disposed
            }
        }

        // Wait for thread to exit
        _trayThread?.Join(2000);
        _initialized.Dispose();

        Logger.Info("[TrayIcon] Tray icon disposed");
    }
}
