namespace OsuMappingHelper.Services;

/// <summary>
/// Provides paths to external tools used by the application.
/// Tools are expected to be in the 'tools' subdirectory of the application directory.
/// </summary>
public static class ToolPaths
{
    private static readonly string ToolsDirectory;

    static ToolPaths()
    {
        // Tools are in the 'tools' subdirectory next to the executable
        ToolsDirectory = Path.Combine(AppContext.BaseDirectory, "tools");
    }

    /// <summary>
    /// Gets the path to the bpm.py script.
    /// </summary>
    public static string BpmScript => Path.Combine(ToolsDirectory, "bpm.py");

    /// <summary>
    /// Gets the path to the msd-calculator executable.
    /// </summary>
    public static string MsdCalculator => Path.Combine(ToolsDirectory, "msd-calculator.exe");

    /// <summary>
    /// Checks if the bpm.py script exists.
    /// </summary>
    public static bool BpmScriptExists => File.Exists(BpmScript);

    /// <summary>
    /// Checks if the msd-calculator executable exists.
    /// </summary>
    public static bool MsdCalculatorExists => File.Exists(MsdCalculator);

    /// <summary>
    /// Gets the tools directory path.
    /// </summary>
    public static string Directory => ToolsDirectory;

    /// <summary>
    /// Validates that all required tools are present.
    /// </summary>
    /// <returns>List of missing tool names, empty if all present.</returns>
    public static List<string> GetMissingTools()
    {
        var missing = new List<string>();
        
        if (!BpmScriptExists)
            missing.Add("bpm.py");
        
        if (!MsdCalculatorExists)
            missing.Add("msd-calculator.exe");
        
        return missing;
    }

    /// <summary>
    /// Logs the status of all tools to the console.
    /// </summary>
    public static void LogToolStatus()
    {
        Console.WriteLine($"[ToolPaths] Tools directory: {ToolsDirectory}");
        Console.WriteLine($"[ToolPaths] bpm.py: {(BpmScriptExists ? "Found" : "MISSING")} at {BpmScript}");
        Console.WriteLine($"[ToolPaths] msd-calculator.exe: {(MsdCalculatorExists ? "Found" : "MISSING")} at {MsdCalculator}");
    }
}
