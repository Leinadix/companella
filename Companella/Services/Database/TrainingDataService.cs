using System.Reflection;
using System.Text.Json;
using Companella.Models.Difficulty;
using Companella.Models.Training;
using Companella.Services.Common;

namespace Companella.Services.Database;

/// <summary>
/// Service for managing dan training data.
/// Handles loading, saving, and merging training entries with MSD skillsets + Interlude rating.
/// </summary>
public class TrainingDataService
{
    private const string TrainingFileName = "training-data.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private TrainingData _trainingData = new();
    private readonly string _trainingDataPath;
    private readonly string _dansPath;
    private string? _loadError;

    /// <summary>
    /// Gets the loaded training data.
    /// </summary>
    public TrainingData TrainingData => _trainingData;

    /// <summary>
    /// Gets whether training data has been loaded successfully.
    /// </summary>
    public bool IsLoaded { get; private set; }

    /// <summary>
    /// Gets the error message if loading failed.
    /// </summary>
    public string? LoadError => _loadError;

    /// <summary>
    /// Gets the total number of training entries.
    /// </summary>
    public int EntryCount => _trainingData.Entries.Count;

    public TrainingDataService()
    {
        // Use exe directory for training data
        var exePath = Assembly.GetExecutingAssembly().Location;
        var exeDir = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory;
        _trainingDataPath = Path.Combine(exeDir, TrainingFileName);
        
        // Use DataPaths for dans.json (stored in AppData)
        _dansPath = DataPaths.DansConfigFile;
    }

    /// <summary>
    /// Initializes the service by loading training data.
    /// </summary>
    public async Task InitializeAsync()
    {
        await LoadAsync();
    }

    /// <summary>
    /// Loads training data from file.
    /// Creates empty training data if file doesn't exist.
    /// </summary>
    public async Task LoadAsync()
    {
        _loadError = null;

        if (!File.Exists(_trainingDataPath))
        {
            // No training data yet - start fresh
            _trainingData = new TrainingData();
            IsLoaded = true;
            Logger.Info("[TrainingData] No existing training data found, starting fresh");
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_trainingDataPath);
            var loaded = JsonSerializer.Deserialize<TrainingData>(json, JsonOptions);

            if (loaded != null)
            {
                _trainingData = loaded;
                IsLoaded = true;
                Logger.Info($"[TrainingData] Loaded {_trainingData.Entries.Count} training entries (v{_trainingData.Version})");
            }
            else
            {
                _trainingData = new TrainingData();
                IsLoaded = true;
                Logger.Info("[TrainingData] Training data file was empty, starting fresh");
            }
        }
        catch (Exception ex)
        {
            _loadError = $"Failed to load training data: {ex.Message}";
            _trainingData = new TrainingData();
            IsLoaded = true; // Allow adding new entries even if load failed
            Logger.Info($"[TrainingData] {_loadError}");
        }
    }

    /// <summary>
    /// Saves training data to file.
    /// </summary>
    public async Task SaveAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_trainingData, JsonOptions);
            await File.WriteAllTextAsync(_trainingDataPath, json);
            Logger.Info($"[TrainingData] Saved {_trainingData.Entries.Count} training entries");
        }
        catch (Exception ex)
        {
            Logger.Info($"[TrainingData] Failed to save training data: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Adds a training entry with all MSD skillset values + Interlude rating.
    /// </summary>
    /// <param name="msdScores">MSD skillset scores from MinaCalc.</param>
    /// <param name="interludeRating">Interlude (YAVSRG) difficulty rating.</param>
    /// <param name="danIndex">The dan index (0-19) assigned by the user.</param>
    /// <param name="sourcePath">Optional source beatmap path.</param>
    public void AddEntry(SkillsetScores msdScores, double interludeRating, int danIndex, string? sourcePath = null)
    {
        var entry = new TrainingEntry
        {
            MsdValues = MsdSkillsetValues.FromSkillsetScores(msdScores),
            InterludeRating = interludeRating,
            DanIndex = danIndex,
            Timestamp = DateTime.UtcNow,
            SourcePath = sourcePath
        };

        _trainingData.Entries.Add(entry);
        var label = DanLookup.GetLabel(danIndex) ?? "?";
        Logger.Info($"[TrainingData] Added entry: MSD Overall {msdScores.Overall:F2} + Interlude {interludeRating:F2} -> Dan {label} (idx={danIndex})");
    }

    /// <summary>
    /// Adds a training entry with MsdSkillsetValues + Interlude rating.
    /// </summary>
    public void AddEntry(MsdSkillsetValues msdValues, double interludeRating, int danIndex, string? sourcePath = null)
    {
        var entry = new TrainingEntry
        {
            MsdValues = msdValues,
            InterludeRating = interludeRating,
            DanIndex = danIndex,
            Timestamp = DateTime.UtcNow,
            SourcePath = sourcePath
        };

        _trainingData.Entries.Add(entry);
        var label = DanLookup.GetLabel(danIndex) ?? "?";
        Logger.Info($"[TrainingData] Added entry: MSD Overall {msdValues.Overall:F2} + Interlude {interludeRating:F2} -> Dan {label} (idx={danIndex})");
    }

    /// <summary>
    /// Gets aggregated training data for each dan.
    /// </summary>
    public Dictionary<int, TrainingAggregateData> GetAggregatedData()
    {
        var result = new Dictionary<int, TrainingAggregateData>();

        for (int danIndex = 0; danIndex < DanLookup.Count; danIndex++)
        {
            var entries = _trainingData.GetEntriesForDan(danIndex);
            var aggregated = TrainingAggregateData.FromEntries(danIndex, entries);

            if (aggregated.EntryCount > 0)
            {
                result[danIndex] = aggregated;
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the dan index (0-19) for a given label.
    /// Returns -1 if not found.
    /// </summary>
    public static int GetDanIndex(string label) => DanLookup.GetIndex(label);

    /// <summary>
    /// Gets the dan label for a given index.
    /// Returns null if index is out of range.
    /// </summary>
    public static string? GetDanLabel(int index) => DanLookup.GetLabel(index);

    /// <summary>
    /// Gets all dan labels in order.
    /// </summary>
    public static IReadOnlyList<string> GetAllDanLabels() => DanLookup.Labels;

    /// <summary>
    /// Merges training data into dans.json.
    /// Aggregates MSD skillsets + Interlude rating for each dan.
    /// </summary>
    /// <param name="enableExtrapolation">If true, extrapolates missing dans using trends from training data.</param>
    public async Task<MergeResult> MergeIntoDansAsync(bool enableExtrapolation = false)
    {
        var result = new MergeResult();

        // Load current dans.json
        DanConfiguration? config;
        try
        {
            if (!File.Exists(_dansPath))
            {
                result.Success = false;
                result.ErrorMessage = "dans.json not found";
                return result;
            }

            var json = await File.ReadAllTextAsync(_dansPath);
            config = JsonSerializer.Deserialize<DanConfiguration>(json, JsonOptions);

            if (config == null || config.Dans.Count == 0)
            {
                result.Success = false;
                result.ErrorMessage = "dans.json is empty or invalid";
                return result;
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Failed to load dans.json: {ex.Message}";
            return result;
        }

        // Get aggregated training data per dan
        var aggregated = GetAggregatedData();

        if (aggregated.Count == 0)
        {
            result.Success = false;
            result.ErrorMessage = "No valid training data to merge";
            return result;
        }

        // Calculate averaged values for each dan
        var danValues = new Dictionary<int, (MsdSkillsetValues Msd, double Interlude, int Count)>();

        foreach (var kvp in aggregated)
        {
            var danIndex = kvp.Key;
            var data = kvp.Value;
            
            if (data.EntryCount == 0)
                continue;

            danValues[danIndex] = (data.AverageMsdValues, data.AverageInterludeRating, data.EntryCount);
        }

        // Enforce monotonicity - ensure higher dans have higher values
        danValues = EnforceMonotonicity(danValues);

        // Update dans.json with calculated values
        foreach (var kvp in danValues)
        {
            var danIndex = kvp.Key;
            var (msd, interlude, _) = kvp.Value;
            var danLabel = DanLookup.GetLabel(danIndex);

            if (danLabel == null)
                continue;

            // Find the dan definition in config
            var danDef = config.Dans.FirstOrDefault(d =>
                d.Label.Equals(danLabel, StringComparison.OrdinalIgnoreCase));

            if (danDef == null)
                continue;

            // Update MSD values (round to 2 decimal places)
            danDef.MsdValues = new MsdSkillsetValues
            {
                Overall = Math.Round(msd.Overall, 2),
                Stream = Math.Round(msd.Stream, 2),
                Jumpstream = Math.Round(msd.Jumpstream, 2),
                Handstream = Math.Round(msd.Handstream, 2),
                Stamina = Math.Round(msd.Stamina, 2),
                Jackspeed = Math.Round(msd.Jackspeed, 2),
                Chordjack = Math.Round(msd.Chordjack, 2),
                Technical = Math.Round(msd.Technical, 2)
            };

            // Update Interlude rating
            danDef.InterludeRating = Math.Round(interlude, 2);

            result.UpdatedDans++;
        }

        // Save updated dans.json
        try
        {
            var outputJson = JsonSerializer.Serialize(config, JsonOptions);
            await File.WriteAllTextAsync(_dansPath, outputJson);
            result.Success = true;
            Logger.Info($"[TrainingData] Merged training data into dans.json: {result.UpdatedDans} dans updated");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Failed to save dans.json: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Clears all training data.
    /// </summary>
    public void ClearAll()
    {
        _trainingData.Entries.Clear();
        Logger.Info("[TrainingData] Cleared all training entries");
    }

    /// <summary>
    /// Gets statistics about the training data.
    /// </summary>
    public TrainingStatistics GetStatistics()
    {
        var stats = new TrainingStatistics
        {
            TotalEntries = _trainingData.Entries.Count,
            UniqueDanLevels = _trainingData.GetUniqueDanIndices().Count()
        };

        // Count entries per dan
        for (int danIndex = 0; danIndex < DanLookup.Count; danIndex++)
        {
            var count = _trainingData.GetEntryCount(danIndex);
            if (count > 0)
            {
                stats.EntriesPerDan[danIndex] = count;
            }
        }

        return stats;
    }

    /// <summary>
    /// Enforces monotonicity: ensures higher dans have higher values than lower dans.
    /// </summary>
    private Dictionary<int, (MsdSkillsetValues Msd, double Interlude, int Count)> EnforceMonotonicity(
        Dictionary<int, (MsdSkillsetValues Msd, double Interlude, int Count)> danValues)
    {
        if (danValues.Count == 0)
            return danValues;

        var corrected = new Dictionary<int, (MsdSkillsetValues Msd, double Interlude, int Count)>();
        var sortedIndices = danValues.Keys.OrderBy(k => k).ToList();

        // Process dans in order, ensuring each is >= previous
        for (int i = 0; i < sortedIndices.Count; i++)
        {
            int danIndex = sortedIndices[i];
            var (msd, interlude, count) = danValues[danIndex];

            if (i > 0)
            {
                int prevIndex = sortedIndices[i - 1];
                var (prevMsd, prevInterlude, _) = corrected[prevIndex];

                // Ensure each MSD component is at least as high as previous dan
                msd = new MsdSkillsetValues
                {
                    Overall = Math.Max(msd.Overall, prevMsd.Overall),
                    Stream = Math.Max(msd.Stream, prevMsd.Stream),
                    Jumpstream = Math.Max(msd.Jumpstream, prevMsd.Jumpstream),
                    Handstream = Math.Max(msd.Handstream, prevMsd.Handstream),
                    Stamina = Math.Max(msd.Stamina, prevMsd.Stamina),
                    Jackspeed = Math.Max(msd.Jackspeed, prevMsd.Jackspeed),
                    Chordjack = Math.Max(msd.Chordjack, prevMsd.Chordjack),
                    Technical = Math.Max(msd.Technical, prevMsd.Technical)
                };

                // Ensure Interlude is at least as high as previous
                interlude = Math.Max(interlude, prevInterlude);
            }

            corrected[danIndex] = (msd, interlude, count);
        }

        return corrected;
    }

    /// <summary>
    /// Removes outliers from training entries using IQR method on Overall MSD.
    /// </summary>
    private List<TrainingEntry> RemoveOutliers(List<TrainingEntry> entries)
    {
        if (entries.Count < 4)
            return entries;

        // Use Overall MSD for outlier detection
        var overallValues = entries.Select(e => e.MsdValues.Overall).OrderBy(v => v).ToList();
        var (q1, q3) = CalculateQuartiles(overallValues);
        double iqr = q3 - q1;
        double lowerBound = q1 - 1.5 * iqr;
        double upperBound = q3 + 1.5 * iqr;

        var result = entries.Where(e => 
            e.MsdValues.Overall >= lowerBound && 
            e.MsdValues.Overall <= upperBound).ToList();

        // Keep at least 50% of original
        if (result.Count < entries.Count * 0.5)
            return entries;

        return result;
    }

    /// <summary>
    /// Calculates Q1 and Q3 quartiles.
    /// </summary>
    private (double Q1, double Q3) CalculateQuartiles(List<double> sortedValues)
    {
        int count = sortedValues.Count;
        if (count == 0)
            return (0, 0);

        int q1End = count / 2;
        double q1 = CalculateMedian(sortedValues, 0, q1End);

        int q3Start = (count + 1) / 2;
        double q3 = CalculateMedian(sortedValues, q3Start, count);

        return (q1, q3);
    }

    /// <summary>
    /// Calculates the median of a sorted list within a range.
    /// </summary>
    private double CalculateMedian(List<double> sortedValues, int start, int end)
    {
        int count = end - start;
        if (count == 0)
            return 0;

        int mid = start + count / 2;
        if (count % 2 == 0)
        {
            return (sortedValues[mid - 1] + sortedValues[mid]) / 2.0;
        }
        else
        {
            return sortedValues[mid];
        }
    }
}

/// <summary>
/// Result of merging training data into dans.json.
/// </summary>
public class MergeResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int UpdatedDans { get; set; }
}

/// <summary>
/// Statistics about training data.
/// </summary>
public class TrainingStatistics
{
    public int TotalEntries { get; set; }
    public int UniqueDanLevels { get; set; }
    public Dictionary<int, int> EntriesPerDan { get; set; } = new();
}
