using System.Reflection;
using OsuMappingHelper.Models;
using Squirrel;

// Alias to resolve conflict between OsuMappingHelper.Models.UpdateInfo and Squirrel.UpdateInfo
using AppUpdateInfo = OsuMappingHelper.Models.UpdateInfo;

namespace OsuMappingHelper.Services;

/// <summary>
/// Service for checking and applying automatic updates using Squirrel.Windows.
/// Updates are fetched from GitHub releases.
/// </summary>
public class SquirrelUpdaterService : IDisposable
{
    private const string GITHUB_REPO_URL = "https://github.com/Leinadix/companella";
    private const string VERSION_FILE_NAME = "version.txt";

    private readonly string _versionFilePath;
    private bool _isDisposed;
    private bool _updatePending;
    private ReleaseEntry? _pendingUpdate;

    /// <summary>
    /// Gets the current application version.
    /// </summary>
    public string CurrentVersion { get; private set; } = "unknown";

    /// <summary>
    /// Gets whether an update is pending (downloaded and ready to apply on restart).
    /// </summary>
    public bool IsUpdatePending => _updatePending;

    /// <summary>
    /// Event raised when download progress changes.
    /// </summary>
    public event EventHandler<DownloadProgressEventArgs>? DownloadProgressChanged;

    /// <summary>
    /// Event raised when an update is available.
    /// </summary>
    public event EventHandler<AppUpdateInfo>? UpdateAvailable;

    /// <summary>
    /// Event raised when an error occurs during update check or download.
    /// </summary>
    public event EventHandler<string>? UpdateError;

    public SquirrelUpdaterService()
    {
        _versionFilePath = DataPaths.VersionFile;
        LoadCurrentVersion();
    }

    /// <summary>
    /// Loads the current version from the version file.
    /// </summary>
    private void LoadCurrentVersion()
    {
        try
        {
            if (File.Exists(_versionFilePath))
            {
                CurrentVersion = File.ReadAllText(_versionFilePath).Trim();
            }
            else
            {
                // Try to get version from assembly
                var assembly = Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                if (version != null)
                {
                    CurrentVersion = $"v{version.Major}.{version.Minor}";
                }
            }
        }
        catch
        {
            CurrentVersion = "unknown";
        }

        Console.WriteLine($"[SquirrelUpdater] Current version: {CurrentVersion}");
    }

    /// <summary>
    /// Checks for updates from GitHub.
    /// </summary>
    /// <returns>Update info if an update is available, null otherwise.</returns>
    public async Task<AppUpdateInfo?> CheckForUpdatesAsync()
    {
        // If not a Squirrel installation, skip update check
        if (!DataPaths.IsSquirrelInstallation())
        {
            Console.WriteLine("[SquirrelUpdater] Not a Squirrel installation, skipping update check");
            Console.WriteLine("[SquirrelUpdater] To receive automatic updates, please install using Setup.exe");
            return null;
        }

        try
        {
            Console.WriteLine($"[SquirrelUpdater] Checking for updates from: {GITHUB_REPO_URL}");

            using var updateManager = await UpdateManager.GitHubUpdateManager(GITHUB_REPO_URL);
            
            var updateInfo = await updateManager.CheckForUpdate();
            
            if (updateInfo == null || updateInfo.ReleasesToApply.Count == 0)
            {
                Console.WriteLine("[SquirrelUpdater] No updates available");
                return null;
            }

            var latestRelease = updateInfo.ReleasesToApply.Last();
            var newVersion = latestRelease.Version.ToString();

            Console.WriteLine($"[SquirrelUpdater] Update available: {newVersion} (current: {CurrentVersion})");

            // Fetch release notes from GitHub API
            var releaseNotes = await FetchReleaseNotesAsync(newVersion);

            var result = new AppUpdateInfo
            {
                TagName = $"v{newVersion}",
                Name = $"Companella v{newVersion}",
                Body = releaseNotes,
                Prerelease = false,
                PublishedAt = DateTime.UtcNow,
                DownloadUrl = "", // Not used with Squirrel
                DownloadSize = latestRelease.Filesize,
                HtmlUrl = $"{GITHUB_REPO_URL}/releases/tag/v{newVersion}"
            };

            UpdateAvailable?.Invoke(this, result);
            return result;
        }
        catch (Exception ex)
        {
            var error = $"Failed to check for updates: {ex.Message}";
            Console.WriteLine($"[SquirrelUpdater] {error}");
            UpdateError?.Invoke(this, error);
            return null;
        }
    }

    /// <summary>
    /// Downloads and prepares an update for installation on restart.
    /// </summary>
    /// <param name="updateInfo">The update information.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <returns>True if the update was successfully downloaded and prepared.</returns>
    public async Task<bool> DownloadAndApplyUpdateAsync(AppUpdateInfo updateInfo, IProgress<DownloadProgressEventArgs>? progress = null)
    {
        if (!DataPaths.IsSquirrelInstallation())
        {
            UpdateError?.Invoke(this, "Cannot update: not a Squirrel installation");
            return false;
        }

        try
        {
            Console.WriteLine($"[SquirrelUpdater] Downloading update: {updateInfo.TagName}");

            ReportProgress(progress, 0, "Preparing update...");

            using var updateManager = await UpdateManager.GitHubUpdateManager(GITHUB_REPO_URL);

            // Check for updates again to get the release entries
            var updateResult = await updateManager.CheckForUpdate();
            
            if (updateResult == null || updateResult.ReleasesToApply.Count == 0)
            {
                UpdateError?.Invoke(this, "No update available");
                return false;
            }

            ReportProgress(progress, 10, "Downloading update...");

            // Download the updates
            await updateManager.DownloadReleases(updateResult.ReleasesToApply, p =>
            {
                // p is 0-100
                var adjustedProgress = 10 + (int)(p * 0.7); // 10-80%
                ReportProgress(progress, adjustedProgress, $"Downloading... {p}%");
            });

            ReportProgress(progress, 80, "Applying update...");

            // Apply the updates (stages them for next restart)
            await updateManager.ApplyReleases(updateResult, p =>
            {
                var adjustedProgress = 80 + (int)(p * 0.2); // 80-100%
                ReportProgress(progress, adjustedProgress, $"Applying... {p}%");
            });

            _updatePending = true;
            _pendingUpdate = updateResult.ReleasesToApply.Last();

            ReportProgress(progress, 100, "Update ready! Restart to apply.");

            Console.WriteLine("[SquirrelUpdater] Update downloaded and staged, will apply on restart");
            return true;
        }
        catch (Exception ex)
        {
            var error = $"Failed to download/apply update: {ex.Message}";
            Console.WriteLine($"[SquirrelUpdater] {error}");
            UpdateError?.Invoke(this, error);
            return false;
        }
    }

    /// <summary>
    /// Restarts the application to apply a pending update.
    /// </summary>
    public void RestartToApplyUpdate()
    {
        if (!_updatePending)
        {
            Console.WriteLine("[SquirrelUpdater] No pending update to apply");
            return;
        }

        try
        {
            Console.WriteLine("[SquirrelUpdater] Restarting to apply update...");

            // UpdateManager.RestartApp() handles the restart properly for Squirrel
            UpdateManager.RestartApp();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SquirrelUpdater] Failed to restart: {ex.Message}");
            UpdateError?.Invoke(this, $"Failed to restart: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts the update process and restarts the application.
    /// This is called when the user confirms they want to restart and apply the update.
    /// </summary>
    /// <param name="updateInfo">The update information (used for compatibility with existing UI).</param>
    public void StartUpdateAndRestart(AppUpdateInfo updateInfo)
    {
        RestartToApplyUpdate();
    }

    /// <summary>
    /// Cancels any ongoing download (not directly supported by Squirrel, but included for API compatibility).
    /// </summary>
    public void CancelDownload()
    {
        // Squirrel doesn't support cancellation directly
        // This is here for API compatibility with the existing UpdateDialog
        Console.WriteLine("[SquirrelUpdater] Download cancellation requested (not fully supported by Squirrel)");
    }

    /// <summary>
    /// Fetches release notes from GitHub API.
    /// </summary>
    private async Task<string> FetchReleaseNotesAsync(string version)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Companella-Updater");
            httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");

            // Squirrel version is 3-part (5.67.0), but GitHub tags are 2-part (v5.67)
            // Try to convert and try both formats
            var versionParts = version.Split('.');
            var twoPartVersion = versionParts.Length >= 2 
                ? $"{versionParts[0]}.{versionParts[1]}" 
                : version;

            // Try two-part version first (v5.67)
            var apiUrl = $"https://api.github.com/repos/Leinadix/companella/releases/tags/v{twoPartVersion}";
            Console.WriteLine($"[SquirrelUpdater] Fetching release notes from: {apiUrl}");
            var response = await httpClient.GetAsync(apiUrl);

            // If not found, try three-part version (v5.67.0)
            if (!response.IsSuccessStatusCode && twoPartVersion != version)
            {
                apiUrl = $"https://api.github.com/repos/Leinadix/companella/releases/tags/v{version}";
                Console.WriteLine($"[SquirrelUpdater] Retrying with: {apiUrl}");
                response = await httpClient.GetAsync(apiUrl);
            }

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var release = System.Text.Json.JsonSerializer.Deserialize<GitHubRelease>(json);
                return release?.Body ?? "No release notes available.";
            }
            else
            {
                Console.WriteLine($"[SquirrelUpdater] GitHub API returned {response.StatusCode} for {apiUrl}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SquirrelUpdater] Failed to fetch release notes: {ex.Message}");
        }

        return "No release notes available.";
    }

    /// <summary>
    /// Reports download progress to subscribers.
    /// </summary>
    private void ReportProgress(IProgress<DownloadProgressEventArgs>? progress, int percentage, string status)
    {
        var args = new DownloadProgressEventArgs
        {
            BytesDownloaded = percentage,
            TotalBytes = 100,
            ProgressPercentage = percentage,
            Status = status
        };

        progress?.Report(args);
        DownloadProgressChanged?.Invoke(this, args);
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
    }
}

/// <summary>
/// Event arguments for download progress updates.
/// </summary>
public class DownloadProgressEventArgs : EventArgs
{
    /// <summary>
    /// The number of bytes downloaded so far.
    /// </summary>
    public long BytesDownloaded { get; set; }

    /// <summary>
    /// The total number of bytes to download.
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// The download progress percentage (0-100).
    /// </summary>
    public int ProgressPercentage { get; set; }

    /// <summary>
    /// A status message describing the current operation.
    /// </summary>
    public string Status { get; set; } = string.Empty;
}

