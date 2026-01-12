using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Companella.Models.Difficulty;

namespace Companella.Services;

/// <summary>
/// Options for MSD analysis.
/// </summary>
public class MsdAnalysisOptions
{
    /// <summary>
    /// Specific rate to analyze (null for all rates).
    /// Valid values: 0.7, 0.8, 0.9, 1.0, 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9, 2.0
    /// </summary>
    public float? Rate { get; set; }

    /// <summary>
    /// Timeout in milliseconds (default 60 seconds).
    /// </summary>
    public int TimeoutMs { get; set; } = 60000;
}

/// <summary>
/// Executes the msd-calculator to analyze osu!mania beatmaps for MSD difficulty ratings.
/// </summary>
public class MsdAnalyzer
{
    private readonly string _executablePath;

    /// <summary>
    /// Creates a new MsdAnalyzer.
    /// </summary>
    /// <param name="executablePath">Path to msd-calculator executable.</param>
    public MsdAnalyzer(string executablePath)
    {
        _executablePath = executablePath;

        if (!File.Exists(_executablePath))
            throw new FileNotFoundException($"MSD calculator executable not found: {_executablePath}");
    }

    /// <summary>
    /// Analyzes an osu!mania beatmap file and returns full MSD data for all rates.
    /// </summary>
    public async Task<MsdResult> AnalyzeAsync(string beatmapPath, MsdAnalysisOptions? options = null)
    {
        options ??= new MsdAnalysisOptions();

        if (!File.Exists(beatmapPath))
            throw new FileNotFoundException($"Beatmap file not found: {beatmapPath}");

        var arguments = $"\"{beatmapPath}\"";

        if (options.Rate.HasValue)
            arguments += $" --rate {options.Rate.Value.ToString(CultureInfo.InvariantCulture)}";

        Logger.Info($"[MSD] Running: {_executablePath} {arguments}");

        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(_executablePath) ?? Environment.CurrentDirectory
        };

        using var process = new Process { StartInfo = startInfo };
        var outputBuilder = new System.Text.StringBuilder();
        var errorBuilder = new System.Text.StringBuilder();

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                outputBuilder.AppendLine(e.Data);
                if (outputBuilder.Length < 500)
                    Logger.Info($"[MSD stdout] {e.Data}");
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                errorBuilder.AppendLine(e.Data);
                Logger.Info($"[MSD stderr] {e.Data}");
            }
        };

        Logger.Info("[MSD] Starting process...");
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var completed = await Task.Run(() => process.WaitForExit(options.TimeoutMs));

        Logger.Info($"[MSD] Process completed: {completed}, Exit code: {(completed ? process.ExitCode.ToString() : "N/A")}");

        if (!completed)
        {
            process.Kill();
            throw new TimeoutException($"MSD analysis timed out after {options.TimeoutMs}ms");
        }

        if (process.ExitCode != 0)
        {
            var error = errorBuilder.ToString();
            Logger.Info($"[MSD] Error output: {error}");
            throw new InvalidOperationException($"MSD analysis failed with exit code {process.ExitCode}: {error}");
        }

        var jsonOutput = outputBuilder.ToString().Trim();
        Logger.Info($"[MSD] Output length: {jsonOutput.Length} chars");

        if (string.IsNullOrEmpty(jsonOutput))
            throw new InvalidOperationException("MSD analysis returned empty output.");

        try
        {
            // If a specific rate was requested, we get SingleRateMsdResult
            // Convert it to MsdResult for consistent API
            if (options.Rate.HasValue)
            {
                var singleResult = JsonSerializer.Deserialize<SingleRateMsdResult>(jsonOutput);
                if (singleResult == null)
                    throw new InvalidOperationException("Failed to parse single-rate MSD result.");

                // Convert to full MsdResult with just one rate
                return new MsdResult
                {
                    BeatmapPath = singleResult.BeatmapPath,
                    MinaCalcVersion = singleResult.MinaCalcVersion,
                    DominantSkillset = singleResult.DominantSkillset,
                    Difficulty1x = singleResult.Scores.Overall,
                    Rates = new List<RateEntry>
                    {
                        new RateEntry
                        {
                            Rate = singleResult.Rate,
                            Scores = singleResult.Scores
                        }
                    }
                };
            }
            else
            {
                var result = JsonSerializer.Deserialize<MsdResult>(jsonOutput);
                Logger.Info($"[MSD] Parsed {result?.Rates?.Count ?? 0} rate entries");
                return result ?? throw new InvalidOperationException("Failed to parse MSD analysis result.");
            }
        }
        catch (JsonException ex)
        {
            Logger.Info($"[MSD] JSON parse error: {ex.Message}");
            throw new InvalidOperationException($"Failed to parse MSD analysis JSON: {ex.Message}\nOutput: {jsonOutput}");
        }
    }

    /// <summary>
    /// Analyzes an osu!mania beatmap and returns MSD for a specific rate only.
    /// </summary>
    public async Task<SingleRateMsdResult> AnalyzeSingleRateAsync(string beatmapPath, float rate, int timeoutMs = 60000)
    {
        if (!File.Exists(beatmapPath))
            throw new FileNotFoundException($"Beatmap file not found: {beatmapPath}");

        var arguments = $"\"{beatmapPath}\" --rate {rate.ToString(CultureInfo.InvariantCulture)}";

        Logger.Info($"[MSD] Running: {_executablePath} {arguments}");

        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(_executablePath) ?? Environment.CurrentDirectory
        };

        using var process = new Process { StartInfo = startInfo };
        var outputBuilder = new System.Text.StringBuilder();
        var errorBuilder = new System.Text.StringBuilder();

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
                outputBuilder.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
                errorBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var completed = await Task.Run(() => process.WaitForExit(timeoutMs));

        if (!completed)
        {
            process.Kill();
            throw new TimeoutException($"MSD analysis timed out after {timeoutMs}ms");
        }

        if (process.ExitCode != 0)
        {
            var error = errorBuilder.ToString();
            throw new InvalidOperationException($"MSD analysis failed with exit code {process.ExitCode}: {error}");
        }

        var jsonOutput = outputBuilder.ToString().Trim();

        if (string.IsNullOrEmpty(jsonOutput))
            throw new InvalidOperationException("MSD analysis returned empty output.");

        try
        {
            var result = JsonSerializer.Deserialize<SingleRateMsdResult>(jsonOutput);
            return result ?? throw new InvalidOperationException("Failed to parse single-rate MSD result.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse MSD analysis JSON: {ex.Message}\nOutput: {jsonOutput}");
        }
    }

    /// <summary>
    /// Synchronous version of AnalyzeAsync.
    /// </summary>
    public MsdResult Analyze(string beatmapPath, MsdAnalysisOptions? options = null)
    {
        return AnalyzeAsync(beatmapPath, options).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Synchronous version of AnalyzeSingleRateAsync.
    /// </summary>
    public SingleRateMsdResult AnalyzeSingleRate(string beatmapPath, float rate, int timeoutMs = 60000)
    {
        return AnalyzeSingleRateAsync(beatmapPath, rate, timeoutMs).GetAwaiter().GetResult();
    }
}
