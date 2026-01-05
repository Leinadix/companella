namespace OsuMappingHelper.Services;

/// <summary>
/// Provides paths to external tools used by the application.
/// Tools are expected to be in the 'tools' subdirectory of the application directory.
/// </summary>
public static class ToolPaths
{
    private static readonly string ToolsDirectory;

    /// <summary>
    /// Currently selected MinaCalc version ("515" or "505").
    /// Set this from user settings on application startup.
    /// </summary>
    public static string SelectedMinaCalcVersion { get; set; } = "515";

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
    /// Gets the path to the msd-calculator-515 executable (MinaCalc 5.15).
    /// </summary>
    public static string MsdCalculator515 => Path.Combine(ToolsDirectory, "msd-calculator-515.exe");

    /// <summary>
    /// Gets the path to the msd-calculator-505 executable (MinaCalc 5.05 legacy).
    /// </summary>
    public static string MsdCalculator505 => Path.Combine(ToolsDirectory, "msd-calculator-505.exe");

    /// <summary>
    /// Gets the path to the selected msd-calculator executable based on SelectedMinaCalcVersion.
    /// </summary>
    public static string MsdCalculator => SelectedMinaCalcVersion == "505" ? MsdCalculator505 : MsdCalculator515;

    /// <summary>
    /// Gets the path to a specific msd-calculator version.
    /// </summary>
    /// <param name="version">"515" for MinaCalc 5.15, "505" for MinaCalc 5.05</param>
    public static string GetMsdCalculator(string version) => version == "505" ? MsdCalculator505 : MsdCalculator515;

    /// <summary>
    /// Checks if the bpm.py script exists.
    /// </summary>
    public static bool BpmScriptExists => File.Exists(BpmScript);

    /// <summary>
    /// Checks if the currently selected msd-calculator executable exists.
    /// </summary>
    public static bool MsdCalculatorExists => File.Exists(MsdCalculator);

    /// <summary>
    /// Checks if the msd-calculator-515 executable exists.
    /// </summary>
    public static bool MsdCalculator515Exists => File.Exists(MsdCalculator515);

    /// <summary>
    /// Checks if the msd-calculator-505 executable exists.
    /// </summary>
    public static bool MsdCalculator505Exists => File.Exists(MsdCalculator505);

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
        
        if (!MsdCalculator515Exists)
            missing.Add("msd-calculator-515.exe");
        
        if (!MsdCalculator505Exists)
            missing.Add("msd-calculator-505.exe");
        
        return missing;
    }

    /// <summary>
    /// Logs the status of all tools to the console.
    /// </summary>
    public static void LogToolStatus()
    {
        Console.WriteLine($"[ToolPaths] Tools directory: {ToolsDirectory}");
        Console.WriteLine($"[ToolPaths] Selected MinaCalc version: {SelectedMinaCalcVersion}");
        Console.WriteLine($"[ToolPaths] bpm.py: {(BpmScriptExists ? "Found" : "MISSING")} at {BpmScript}");
        Console.WriteLine($"[ToolPaths] msd-calculator-515.exe: {(MsdCalculator515Exists ? "Found" : "MISSING")} at {MsdCalculator515}");
        Console.WriteLine($"[ToolPaths] msd-calculator-505.exe: {(MsdCalculator505Exists ? "Found" : "MISSING")} at {MsdCalculator505}");
    }

    /// <summary>
    /// Checks if the given key count is supported by the currently selected MinaCalc version.
    /// MinaCalc 5.15+ supports 4K, 6K, and 7K.
    /// MinaCalc 5.05 only supports 4K.
    /// </summary>
    /// <param name="keyCount">The key count (CircleSize value) to check.</param>
    /// <returns>True if supported, false otherwise.</returns>
    public static bool IsKeyCountSupported(double keyCount)
    {
        int keys = (int)Math.Round(keyCount);
        
        if (SelectedMinaCalcVersion == "515")
        {
            // MinaCalc 5.15 supports 4K, 6K, and 7K
            return keys == 4 || keys == 6 || keys == 7;
        }
        else
        {
            // MinaCalc 5.05 only supports 4K
            return keys == 4;
        }
    }

    /// <summary>
    /// Gets a display string for supported key counts based on the selected MinaCalc version.
    /// </summary>
    public static string SupportedKeyCountsDisplay => SelectedMinaCalcVersion == "515" ? "4K/6K/7K" : "4K";
}
