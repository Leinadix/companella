namespace OsuMappingHelper.Services;

/// <summary>
/// Simple file-based logger for GUI applications where console output is not visible.
/// Logs are written to %AppData%\Companella\companella.log
/// </summary>
public static class Logger
{
    private static readonly object _lockObj = new();
    private static readonly string _logFilePath;
    private static readonly long MaxLogFileSize = 5 * 1024 * 1024; // 5 MB

    static Logger()
    {
        var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var companellaFolder = Path.Combine(appDataRoot, "Companella");
        
        if (!Directory.Exists(companellaFolder))
        {
            Directory.CreateDirectory(companellaFolder);
        }

        _logFilePath = Path.Combine(companellaFolder, "companella.log");
        
        // Rotate log file if it's too large
        RotateLogIfNeeded();
    }

    /// <summary>
    /// Gets the path to the log file.
    /// </summary>
    public static string LogFilePath => _logFilePath;

    /// <summary>
    /// Writes an informational message to the log file.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public static void Info(string message)
    {
        WriteLog("INFO", message);
    }

    /// <summary>
    /// Writes a warning message to the log file.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public static void Warn(string message)
    {
        WriteLog("WARN", message);
    }

    /// <summary>
    /// Writes an error message to the log file.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public static void Error(string message)
    {
        WriteLog("ERROR", message);
    }

    /// <summary>
    /// Writes an error message with exception details to the log file.
    /// </summary>
    /// <param name="message">The message to log.</param>
    /// <param name="ex">The exception to log.</param>
    public static void Error(string message, Exception ex)
    {
        WriteLog("ERROR", $"{message}: {ex.Message}");
        WriteLog("ERROR", $"StackTrace: {ex.StackTrace}");
    }

    /// <summary>
    /// Writes a debug message to the log file.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public static void Debug(string message)
    {
        WriteLog("DEBUG", message);
    }

    /// <summary>
    /// Writes a log entry with the specified level.
    /// </summary>
    private static void WriteLog(string level, string message)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logEntry = $"[{timestamp}] [{level}] {message}";

            lock (_lockObj)
            {
                File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
            }
        }
        catch
        {
            // Swallow logging exceptions to prevent crashes
        }
    }

    /// <summary>
    /// Rotates the log file if it exceeds the maximum size.
    /// Keeps one backup file (.log.old).
    /// </summary>
    private static void RotateLogIfNeeded()
    {
        try
        {
            if (!File.Exists(_logFilePath))
                return;

            var fileInfo = new FileInfo(_logFilePath);
            if (fileInfo.Length <= MaxLogFileSize)
                return;

            var oldLogPath = _logFilePath + ".old";
            
            // Delete old backup if it exists
            if (File.Exists(oldLogPath))
            {
                File.Delete(oldLogPath);
            }

            // Rename current log to backup
            File.Move(_logFilePath, oldLogPath);
        }
        catch
        {
            // Swallow rotation exceptions
        }
    }

    /// <summary>
    /// Clears the log file.
    /// </summary>
    public static void Clear()
    {
        try
        {
            lock (_lockObj)
            {
                File.WriteAllText(_logFilePath, string.Empty);
            }
        }
        catch
        {
            // Swallow exceptions
        }
    }
}
