using System.Reflection;

namespace OsuMappingHelper.Services;

/// <summary>
/// Provides centralized paths for user data files.
/// User data is stored in %AppData%\Companella to survive Squirrel updates.
/// Application files remain in the installation directory.
/// </summary>
public static class DataPaths
{
    private static readonly Lazy<string> _appDataFolder = new(InitializeAppDataFolder);
    private static readonly Lazy<string> _applicationFolder = new(GetApplicationFolder);

    /// <summary>
    /// The application data folder (%AppData%\Companella).
    /// User data that should persist across updates is stored here.
    /// </summary>
    public static string AppDataFolder => _appDataFolder.Value;

    /// <summary>
    /// The application installation folder (where the exe is located).
    /// Application files like tools, fonts, etc. are here.
    /// </summary>
    public static string ApplicationFolder => _applicationFolder.Value;

    /// <summary>
    /// Path to settings.json in AppData.
    /// </summary>
    public static string SettingsFile => Path.Combine(AppDataFolder, "settings.json");

    /// <summary>
    /// Path to dans.json in AppData.
    /// </summary>
    public static string DansConfigFile => Path.Combine(AppDataFolder, "dans.json");

    /// <summary>
    /// Path to sessions.db in AppData.
    /// </summary>
    public static string SessionsDatabase => Path.Combine(AppDataFolder, "sessions.db");

    /// <summary>
    /// Path to maps.db in AppData.
    /// </summary>
    public static string MapsDatabase => Path.Combine(AppDataFolder, "maps.db");

    /// <summary>
    /// Path to version.txt in the application folder.
    /// </summary>
    public static string VersionFile => Path.Combine(ApplicationFolder, "version.txt");

    /// <summary>
    /// Gets the path to the default dans.json that ships with the application.
    /// This is used as a template for first-run or reset scenarios.
    /// </summary>
    public static string DefaultDansConfigFile => Path.Combine(ApplicationFolder, "dans.json");

    /// <summary>
    /// Initializes the AppData folder, creating it if it doesn't exist.
    /// </summary>
    private static string InitializeAppDataFolder()
    {
        var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var companellaFolder = Path.Combine(appDataRoot, "Companella");

        if (!Directory.Exists(companellaFolder))
        {
            Directory.CreateDirectory(companellaFolder);
            Logger.Info($"[DataPaths] Created AppData folder: {companellaFolder}");
        }

        return companellaFolder;
    }

    /// <summary>
    /// Gets the application folder (where the exe is located).
    /// </summary>
    private static string GetApplicationFolder()
    {
        var exePath = Assembly.GetExecutingAssembly().Location;
        
        // Handle single-file deployment where Location may be empty
        if (string.IsNullOrEmpty(exePath))
        {
            exePath = Environment.ProcessPath;
        }

        return Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
    }

    /// <summary>
    /// Migrates user data from old locations (next to exe) to AppData.
    /// Call this on application startup to handle upgrades from pre-Squirrel versions.
    /// </summary>
    public static void MigrateUserDataIfNeeded()
    {
        Logger.Info("[DataPaths] Checking for user data migration...");

        // Files to migrate
        var migrations = new[]
        {
            ("settings.json", SettingsFile),
            ("dans.json", DansConfigFile),
            ("sessions.db", SessionsDatabase),
            ("maps.db", MapsDatabase)
        };

        var migratedCount = 0;

        foreach (var (filename, targetPath) in migrations)
        {
            var oldPath = Path.Combine(ApplicationFolder, filename);

            // Only migrate if:
            // 1. Old file exists in app folder
            // 2. New file doesn't exist in AppData (don't overwrite existing data)
            if (File.Exists(oldPath) && !File.Exists(targetPath))
            {
                try
                {
                    File.Copy(oldPath, targetPath, overwrite: false);
                    Logger.Info($"[DataPaths] Migrated {filename} to AppData");
                    migratedCount++;

                    // Optionally delete the old file after successful migration
                    // Keeping it for now as a backup
                    // File.Delete(oldPath);
                }
                catch (Exception ex)
                {
                    Logger.Info($"[DataPaths] Failed to migrate {filename}: {ex.Message}");
                }
            }
        }

        // Special case: Copy default dans.json if no dans.json exists anywhere
        if (!File.Exists(DansConfigFile) && File.Exists(DefaultDansConfigFile))
        {
            try
            {
                File.Copy(DefaultDansConfigFile, DansConfigFile);
                Logger.Info("[DataPaths] Copied default dans.json to AppData");
                migratedCount++;
            }
            catch (Exception ex)
            {
                Logger.Info($"[DataPaths] Failed to copy default dans.json: {ex.Message}");
            }
        }

        if (migratedCount > 0)
        {
            Logger.Info($"[DataPaths] Migration complete: {migratedCount} files migrated");
        }
        else
        {
            Logger.Info("[DataPaths] No migration needed");
        }
    }

    /// <summary>
    /// Checks if the application is running from a Squirrel-managed installation.
    /// </summary>
    public static bool IsSquirrelInstallation()
    {
        // Squirrel installs to %LocalAppData%\AppName\app-X.X.X
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return ApplicationFolder.StartsWith(localAppData, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the Squirrel Update.exe path if running from a Squirrel installation.
    /// </summary>
    public static string? GetSquirrelUpdateExePath()
    {
        if (!IsSquirrelInstallation())
            return null;

        // Update.exe is in the parent directory of the app-X.X.X folder
        var parentDir = Directory.GetParent(ApplicationFolder)?.FullName;
        if (parentDir == null)
            return null;

        var updateExe = Path.Combine(parentDir, "Update.exe");
        return File.Exists(updateExe) ? updateExe : null;
    }
}

