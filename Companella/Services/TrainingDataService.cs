using System.Reflection;
using System.Text.Json;
using Companella.Models.Training;

namespace Companella.Services;

/// <summary>
/// Service for managing dan training data.
/// Handles loading, saving, and merging training entries.
/// </summary>
public class TrainingDataService
{
    private const string TrainingFileName = "training-data.json";
    private const string DansFileName = "dans.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// All dan labels in order from lowest to highest difficulty.
    /// </summary>
    private static readonly string[] DanLabelsOrdered = new[]
    {
        "1", "2", "3", "4", "5", "6", "7", "8", "9", "10",
        "alpha", "beta", "gamma", "delta", "epsilon",
        "zeta", "eta", "theta", "iota", "kappa"
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
        var exePath = Assembly.GetExecutingAssembly().Location;
        var exeDir = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory;
        _trainingDataPath = Path.Combine(exeDir, TrainingFileName);
        _dansPath = Path.Combine(exeDir, DansFileName);
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
                Logger.Info($"[TrainingData] Loaded {_trainingData.Entries.Count} training entries");
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
    /// Adds a training entry for a pattern with YAVSRG rating.
    /// </summary>
    public void AddEntry(string patternType, double yavsrgRating, string danLabel, string? sourcePath = null)
    {
        var entry = new TrainingEntry
        {
            PatternType = patternType,
            YavsrgRating = yavsrgRating,
            DanLabel = danLabel,
            Timestamp = DateTime.UtcNow,
            SourcePath = sourcePath
        };

        _trainingData.Entries.Add(entry);
        Logger.Info($"[TrainingData] Added entry: {patternType} @ {yavsrgRating:F2}* -> Dan {danLabel}");
    }

    /// <summary>
    /// Adds multiple training entries at once.
    /// </summary>
    public void AddEntries(IEnumerable<(string PatternType, double YavsrgRating)> patterns, string danLabel, string? sourcePath = null)
    {
        foreach (var (patternType, yavsrgRating) in patterns)
        {
            AddEntry(patternType, yavsrgRating, danLabel, sourcePath);
        }
    }

    /// <summary>
    /// Gets aggregated training data for all pattern/dan combinations.
    /// </summary>
    public Dictionary<string, Dictionary<string, TrainingPatternData>> GetAggregatedData()
    {
        var result = new Dictionary<string, Dictionary<string, TrainingPatternData>>();

        foreach (var danLabel in DanLabelsOrdered)
        {
            result[danLabel] = new Dictionary<string, TrainingPatternData>();

            foreach (var patternType in _trainingData.GetUniquePatternTypes())
            {
                var entries = _trainingData.GetEntries(patternType, danLabel);
                var aggregated = TrainingPatternData.FromEntries(patternType, danLabel, entries);

                if (aggregated.EntryCount > 0)
                {
                    result[danLabel][patternType] = aggregated;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the dan index (0-19) for a given label.
    /// Returns -1 if not found.
    /// </summary>
    public static int GetDanIndex(string label)
    {
        for (int i = 0; i < DanLabelsOrdered.Length; i++)
        {
            if (DanLabelsOrdered[i].Equals(label, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Gets all dan labels in order.
    /// </summary>
    public static IReadOnlyList<string> GetAllDanLabels() => DanLabelsOrdered;

    /// <summary>
    /// Merges training data into dans.json using 2-step interpolation.
    /// </summary>
    /// <param name="enableExtrapolation">If true, extrapolates missing dans at the beginning/end using trends from training data.</param>
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

        // Get aggregated training data
        var aggregated = GetAggregatedData();

        // Get all unique pattern types from training data
        var patternTypes = _trainingData.GetUniquePatternTypes().ToList();

        foreach (var patternType in patternTypes)
        {
            // Calculate base YAVSRG rating values using weighted average (top 100 entries)
            var baseValues = new Dictionary<int, double>();

            for (int i = 0; i < DanLabelsOrdered.Length; i++)
            {
                var danLabel = DanLabelsOrdered[i];
                if (aggregated.TryGetValue(danLabel, out var danData) &&
                    danData.TryGetValue(patternType, out var patternData) &&
                    patternData.EntryCount > 0)
                {
                    // Get all entries for this pattern/dan combination
                    var entries = _trainingData.GetEntries(patternType, danLabel).ToList();
                    
                    // Only use entries with YAVSRG rating
                    var entriesWithRating = entries.Where(e => e.YavsrgRating > 0).ToList();
                    
                    if (entriesWithRating.Count == 0)
                        continue;
                    
                    // Remove outliers using IQR method
                    var filteredEntries = RemoveOutliersByYavsrg(entriesWithRating);
                    
                    // Sort by rating descending and take top 100
                    var topEntries = filteredEntries.OrderByDescending(e => e.YavsrgRating).Take(100).ToList();
                    
                    // Calculate weighted average: weight scales with total entry count
                    double weight = Math.Min(1.0, Math.Log10(Math.Max(1, filteredEntries.Count)) / Math.Log10(100));
                    
                    // Calculate average rating
                    double averageRating = filteredEntries.Average(e => e.YavsrgRating);
                    
                    // Weighted average: top entries weighted more heavily
                    double weightedRating = topEntries.Count > 0
                        ? topEntries.Average(e => e.YavsrgRating) * weight + averageRating * (1 - weight)
                        : averageRating;
                    
                    baseValues[i] = weightedRating;
                }
            }

            if (baseValues.Count == 0)
                continue;

            // Enforce monotonicity - ensure higher dans have higher ratings
            baseValues = EnforceMonotonicityYavsrg(baseValues);

            // Store the calculated YAVSRG ratings
            foreach (var kvp in baseValues)
            {
                int danIndex = kvp.Key;
                double baseRating = kvp.Value;

                // Find the dan definition in config
                var danDef = config.Dans.FirstOrDefault(d => 
                    d.Label.Equals(DanLabelsOrdered[danIndex], StringComparison.OrdinalIgnoreCase));

                if (danDef == null)
                    continue;

                danDef.Patterns[patternType] = Math.Round(baseRating, 2);
                result.UpdatedPatterns++;
            }

            result.UpdatedDans = baseValues.Count;
        }

        // Save updated dans.json
        try
        {
            var outputJson = JsonSerializer.Serialize(config, JsonOptions);
            await File.WriteAllTextAsync(_dansPath, outputJson);
            result.Success = true;
            Logger.Info($"[TrainingData] Merged training data into dans.json: {result.UpdatedPatterns} pattern updates across {result.UpdatedDans} dans");
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
            UniquePatternTypes = _trainingData.GetUniquePatternTypes().Count(),
            UniqueDanLevels = _trainingData.GetUniqueDanLabels().Count()
        };

        // Count entries per dan
        foreach (var danLabel in DanLabelsOrdered)
        {
            var count = _trainingData.Entries.Count(e => 
                e.DanLabel.Equals(danLabel, StringComparison.OrdinalIgnoreCase));
            if (count > 0)
            {
                stats.EntriesPerDan[danLabel] = count;
            }
        }

        // Count entries per pattern type
        foreach (var patternType in _trainingData.GetUniquePatternTypes())
        {
            var count = _trainingData.Entries.Count(e => 
                e.PatternType.Equals(patternType, StringComparison.OrdinalIgnoreCase));
            stats.EntriesPerPattern[patternType] = count;
        }

        return stats;
    }

    /// <summary>
    /// Enforces monotonicity: ensures higher dans have higher values than lower dans.
    /// Uses weighted averaging based on entry counts to correct violations.
    /// </summary>
    private Dictionary<int, (double Bpm, double Msd, double Weight)> EnforceMonotonicity(
        Dictionary<int, (double Bpm, double Msd, double Weight)> baseValues)
    {
        if (baseValues.Count == 0)
            return baseValues;

        var corrected = new Dictionary<int, (double Bpm, double Msd, double Weight)>();
        var sortedIndices = baseValues.Keys.OrderBy(k => k).ToList();

        // Process dans in order, ensuring each is >= previous
        for (int i = 0; i < sortedIndices.Count; i++)
        {
            int danIndex = sortedIndices[i];
            var (bpm, msd, weight) = baseValues[danIndex];

            if (i > 0)
            {
                // Check against previous dan
                int prevIndex = sortedIndices[i - 1];
                var (prevBpm, prevMsd, prevWeight) = corrected[prevIndex];

                // If current dan has lower values, correct it using weighted average
                if (bpm < prevBpm || msd < prevMsd)
                {
                    // Weighted correction: blend towards higher value based on weights
                    double totalWeight = weight + prevWeight;
                    if (totalWeight > 0)
                    {
                        // Use weighted average, but ensure it's at least as high as previous
                        bpm = Math.Max(prevBpm, (bpm * weight + prevBpm * prevWeight) / totalWeight);
                        msd = Math.Max(prevMsd, (msd * weight + prevMsd * prevWeight) / totalWeight);
                    }
                    else
                    {
                        // Fallback: ensure minimum increase
                        bpm = Math.Max(prevBpm + 0.1, bpm);
                        msd = Math.Max(prevMsd + 0.1, msd);
                    }
                }
            }

            corrected[danIndex] = (bpm, msd, weight);
        }

        return corrected;
    }

    /// <summary>
    /// Removes outliers from training entries using IQR (Interquartile Range) method.
    /// Filters outliers separately for BPM and MSD.
    /// </summary>
    private List<TrainingEntry> RemoveOutliers(List<TrainingEntry> entries)
    {
        if (entries.Count < 4)
            return entries; // Need at least 4 entries for IQR calculation

        var result = new List<TrainingEntry>(entries);

        // Remove BPM outliers
        var bpmValues = entries.Select(e => e.Bpm).OrderBy(v => v).ToList();
        var (bpmQ1, bpmQ3) = CalculateQuartiles(bpmValues);
        double bpmIqr = bpmQ3 - bpmQ1;
        double bpmLowerBound = bpmQ1 - 1.5 * bpmIqr;
        double bpmUpperBound = bpmQ3 + 1.5 * bpmIqr;

        result = result.Where(e => e.Bpm >= bpmLowerBound && e.Bpm <= bpmUpperBound).ToList();

        // Remove MSD outliers from remaining entries
        if (result.Count < 4)
            return entries; // If too many BPM outliers removed, return original

        var msdValues = result.Select(e => e.Msd).OrderBy(v => v).ToList();
        var (msdQ1, msdQ3) = CalculateQuartiles(msdValues);
        double msdIqr = msdQ3 - msdQ1;
        double msdLowerBound = msdQ1 - 1.5 * msdIqr;
        double msdUpperBound = msdQ3 + 1.5 * msdIqr;

        result = result.Where(e => e.Msd >= msdLowerBound && e.Msd <= msdUpperBound).ToList();

        // Ensure we don't remove too many entries (keep at least 50% of original)
        if (result.Count < entries.Count * 0.5)
            return entries; // Too many outliers, return original

        return result;
    }

    /// <summary>
    /// Removes outliers from training entries using YAVSRG rating (IQR method).
    /// </summary>
    private List<TrainingEntry> RemoveOutliersByYavsrg(List<TrainingEntry> entries)
    {
        if (entries.Count < 4)
            return entries; // Need at least 4 entries for IQR calculation

        var ratingValues = entries.Select(e => e.YavsrgRating).OrderBy(v => v).ToList();
        var (q1, q3) = CalculateQuartiles(ratingValues);
        double iqr = q3 - q1;
        double lowerBound = q1 - 1.5 * iqr;
        double upperBound = q3 + 1.5 * iqr;

        var result = entries.Where(e => e.YavsrgRating >= lowerBound && e.YavsrgRating <= upperBound).ToList();

        // Ensure we don't remove too many entries (keep at least 50% of original)
        if (result.Count < entries.Count * 0.5)
            return entries; // Too many outliers, return original

        return result;
    }

    /// <summary>
    /// Enforces monotonicity for YAVSRG ratings - ensures higher dans have higher ratings.
    /// </summary>
    private Dictionary<int, double> EnforceMonotonicityYavsrg(Dictionary<int, double> baseValues)
    {
        if (baseValues.Count == 0)
            return baseValues;

        var corrected = new Dictionary<int, double>();
        var sortedIndices = baseValues.Keys.OrderBy(k => k).ToList();

        // Process dans in order, ensuring each is >= previous
        for (int i = 0; i < sortedIndices.Count; i++)
        {
            int danIndex = sortedIndices[i];
            double rating = baseValues[danIndex];

            if (i > 0)
            {
                // Check against previous dan
                int prevIndex = sortedIndices[i - 1];
                double prevRating = corrected[prevIndex];

                // Ensure current rating is at least as high as previous
                if (rating < prevRating)
                {
                    rating = prevRating;
                }
            }

            corrected[danIndex] = rating;
        }

        return corrected;
    }

    /// <summary>
    /// Calculates Q1 and Q3 quartiles for outlier detection using the median method.
    /// </summary>
    private (double Q1, double Q3) CalculateQuartiles(List<double> sortedValues)
    {
        int count = sortedValues.Count;
        if (count == 0)
            return (0, 0);
        
        // Q1: median of lower half (excluding median if odd count)
        int q1End = count / 2;
        double q1 = CalculateMedian(sortedValues, 0, q1End);
        
        // Q3: median of upper half (excluding median if odd count)
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
            // Even count: average of two middle values
            return (sortedValues[mid - 1] + sortedValues[mid]) / 2.0;
        }
        else
        {
            // Odd count: middle value
            return sortedValues[mid];
        }
    }

    /// <summary>
    /// Calculates the average gap/trend from consecutive data points for extrapolation.
    /// </summary>
    private (double Bpm, double Msd) CalculateAverageGap(
        Dictionary<int, (double Bpm, double Msd, double Weight)> baseValues,
        int currentIndex,
        bool isForward)
    {
        var gaps = new List<(double Bpm, double Msd)>();

        // Collect all consecutive gaps in the direction we need
        var sortedIndices = baseValues.Keys.OrderBy(k => k).ToList();
        
        for (int i = 0; i < sortedIndices.Count - 1; i++)
        {
            int idx1 = sortedIndices[i];
            int idx2 = sortedIndices[i + 1];
            
            // Only use gaps that are in the direction we need
            if (isForward && idx1 >= currentIndex)
            {
                // Forward extrapolation: use gaps after current index
                var (bpm1, msd1, _) = baseValues[idx1];
                var (bpm2, msd2, _) = baseValues[idx2];
                gaps.Add((bpm2 - bpm1, msd2 - msd1));
            }
            else if (!isForward && idx2 <= currentIndex)
            {
                // Backward extrapolation: use gaps before current index
                var (bpm1, msd1, _) = baseValues[idx1];
                var (bpm2, msd2, _) = baseValues[idx2];
                gaps.Add((bpm2 - bpm1, msd2 - msd1));
            }
        }

        if (gaps.Count == 0)
        {
            // Fallback: use any available gap
            if (sortedIndices.Count >= 2)
            {
                var (bpm1, msd1, _) = baseValues[sortedIndices[0]];
                var (bpm2, msd2, _) = baseValues[sortedIndices[sortedIndices.Count - 1]];
                double avgBpmGap = (bpm2 - bpm1) / (sortedIndices.Count - 1);
                double avgMsdGap = (msd2 - msd1) / (sortedIndices.Count - 1);
                return (avgBpmGap, avgMsdGap);
            }
            return (0, 0);
        }

        // Return average gap
        double avgBpm = gaps.Average(g => g.Bpm);
        double avgMsd = gaps.Average(g => g.Msd);
        return (avgBpm, avgMsd);
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
    public int UpdatedPatterns { get; set; }
}

/// <summary>
/// Statistics about training data.
/// </summary>
public class TrainingStatistics
{
    public int TotalEntries { get; set; }
    public int UniquePatternTypes { get; set; }
    public int UniqueDanLevels { get; set; }
    public Dictionary<string, int> EntriesPerDan { get; set; } = new();
    public Dictionary<string, int> EntriesPerPattern { get; set; } = new();
}

