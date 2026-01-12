using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Companella.Models.Difficulty;
using Companella.Services.Common;

namespace Companella.Services.Analysis;

/// <summary>
/// Options for BPM analysis.
/// </summary>
public class BpmAnalysisOptions
{
    /// <summary>
    /// Whether to include average BPM in output.
    /// </summary>
    public bool IncludeAverage { get; set; } = true;

    /// <summary>
    /// Expected BPM hint to guide detection (null for auto-detect).
    /// </summary>
    public double? BpmHint { get; set; }

    /// <summary>
    /// Use harmonic/percussive separation for cleaner beat detection.
    /// </summary>
    public bool UsePercussion { get; set; } = false;

    /// <summary>
    /// Beat tracking tightness (higher = stricter grid, default: 100).
    /// </summary>
    public double Tightness { get; set; } = 100;

    /// <summary>
    /// Whether to detect offbeat/syncopation sections and insert timing points.
    /// Default is true.
    /// </summary>
    public bool DetectOffbeats { get; set; } = true;

    /// <summary>
    /// Whether to stabilize BPM by consolidating small variations.
    /// Default is true.
    /// </summary>
    public bool StabilizeBpm { get; set; } = true;

    /// <summary>
    /// BPM tolerance for stabilization - changes smaller than this are merged.
    /// Default is 2.0 BPM.
    /// </summary>
    public double BpmTolerance { get; set; } = 2.0;

    /// <summary>
    /// Timeout in milliseconds (default 5 minutes).
    /// </summary>
    public int TimeoutMs { get; set; } = 300000;
}

/// <summary>
/// Executes the bpm.exe tool to analyze audio files for BPM data.
/// </summary>
public class BpmAnalyzer
{
    private readonly string _exePath;

    /// <summary>
    /// Creates a new BpmAnalyzer.
    /// </summary>
    /// <param name="exePath">Path to bpm.exe executable.</param>
    public BpmAnalyzer(string exePath)
    {
        _exePath = exePath;

        if (!File.Exists(_exePath))
            throw new FileNotFoundException($"BPM analysis executable not found: {_exePath}");
    }

    /// <summary>
    /// Analyzes an audio file and returns BPM data with advanced options.
    /// </summary>
    public async Task<BpmResult> AnalyzeAsync(string audioPath, BpmAnalysisOptions? options = null)
    {
        options ??= new BpmAnalysisOptions();
        
        if (!File.Exists(audioPath))
            throw new FileNotFoundException($"Audio file not found: {audioPath}");

        var arguments = $"\"{audioPath}\" -j --persist 3 --min-gap-ms 50 --offset-threshold-ms 0 --drift-slope-ms-per-beat 0.7 --bpm-min-change 0.2";
        arguments += " --phase-divisions 8";
        if (options.IncludeAverage)
            arguments += " -a";
        
        if (options.BpmHint.HasValue)
            arguments += $" --bpm-hint {options.BpmHint.Value.ToString(CultureInfo.InvariantCulture)}";
        
        if (options.UsePercussion)
            arguments += " --percussion";
        
        if (Math.Abs(options.Tightness - 100) > 0.01)
            arguments += $" --tightness {options.Tightness.ToString(CultureInfo.InvariantCulture)}";
        
        if (!options.DetectOffbeats)
            arguments += " --no-offbeat";
        
        if (!options.StabilizeBpm)
            arguments += " --no-stabilize";
        
        if (Math.Abs(options.BpmTolerance - 2.0) > 0.01)
            arguments += $" --bpm-tolerance {options.BpmTolerance.ToString(CultureInfo.InvariantCulture)}";

        Logger.Info($"[BPM] Running: {_exePath} {arguments}");
        Logger.Info($"[BPM] Working directory: {Path.GetDirectoryName(_exePath)}");

        var startInfo = new ProcessStartInfo
        {
            FileName = _exePath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(_exePath) ?? Environment.CurrentDirectory
        };

        using var process = new Process { StartInfo = startInfo };
        var outputBuilder = new System.Text.StringBuilder();
        var errorBuilder = new System.Text.StringBuilder();

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                outputBuilder.AppendLine(e.Data);
                // Log first 200 chars of output to avoid flooding console with JSON
                if (outputBuilder.Length < 500)
                    Logger.Info($"[BPM stdout] {e.Data}");
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                errorBuilder.AppendLine(e.Data);
                Logger.Info($"[BPM stderr] {e.Data}");
            }
        };

        Logger.Info("[BPM] Starting process...");
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var completed = await Task.Run(() => process.WaitForExit(options.TimeoutMs));

        Logger.Info($"[BPM] Process completed: {completed}, Exit code: {(completed ? process.ExitCode.ToString() : "N/A")}");

        if (!completed)
        {
            process.Kill();
            throw new TimeoutException($"BPM analysis timed out after {options.TimeoutMs}ms");
        }

        if (process.ExitCode != 0)
        {
            var error = errorBuilder.ToString();
            Logger.Info($"[BPM] Error output: {error}");
            throw new InvalidOperationException($"BPM analysis failed with exit code {process.ExitCode}: {error}");
        }

        var jsonOutput = outputBuilder.ToString().Trim();
        Logger.Info($"[BPM] Output length: {jsonOutput.Length} chars");
        
        if (string.IsNullOrEmpty(jsonOutput))
            throw new InvalidOperationException("BPM analysis returned empty output.");

        try
        {
            var result = JsonSerializer.Deserialize<BpmResult>(jsonOutput);
            Logger.Info($"[BPM] Parsed {result?.Beats?.Count ?? 0} beats");
            return result ?? throw new InvalidOperationException("Failed to parse BPM analysis result.");
        }
        catch (JsonException ex)
        {
            Logger.Info($"[BPM] JSON parse error: {ex.Message}");
            throw new InvalidOperationException($"Failed to parse BPM analysis JSON: {ex.Message}\nOutput: {jsonOutput}");
        }
    }

    /// <summary>
    /// Analyzes an audio file and returns BPM data (simple overload for backwards compatibility).
    /// </summary>
    public async Task<BpmResult> AnalyzeAsync(string audioPath, bool includeAverage, int timeoutMs = 300000)
    {
        return await AnalyzeAsync(audioPath, new BpmAnalysisOptions
        {
            IncludeAverage = includeAverage,
            TimeoutMs = timeoutMs
        });
    }

    /// <summary>
    /// Synchronous version of AnalyzeAsync.
    /// </summary>
    public BpmResult Analyze(string audioPath, BpmAnalysisOptions? options = null)
    {
        return AnalyzeAsync(audioPath, options).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Synchronous version of AnalyzeAsync (simple overload for backwards compatibility).
    /// </summary>
    public BpmResult Analyze(string audioPath, bool includeAverage, int timeoutMs = 300000)
    {
        return AnalyzeAsync(audioPath, includeAverage, timeoutMs).GetAwaiter().GetResult();
    }
}
