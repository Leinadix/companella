using System.Text.Json.Serialization;

namespace OsuMappingHelper.Models;

/// <summary>
/// Root container for dan training data.
/// Stored separately from dans.json to preserve original configuration.
/// </summary>
public class TrainingData
{
    /// <summary>
    /// Schema version for future compatibility.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 2;

    /// <summary>
    /// All training entries collected from user maps.
    /// </summary>
    [JsonPropertyName("entries")]
    public List<TrainingEntry> Entries { get; set; } = new();

    /// <summary>
    /// Gets all entries for a specific pattern type and dan label.
    /// </summary>
    public IEnumerable<TrainingEntry> GetEntries(string patternType, string danLabel)
    {
        return Entries.Where(e => 
            e.PatternType.Equals(patternType, StringComparison.OrdinalIgnoreCase) &&
            e.DanLabel.Equals(danLabel, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets all unique dan labels in the training data.
    /// </summary>
    public IEnumerable<string> GetUniqueDanLabels()
    {
        return Entries.Select(e => e.DanLabel).Distinct();
    }

    /// <summary>
    /// Gets all unique pattern types in the training data.
    /// </summary>
    public IEnumerable<string> GetUniquePatternTypes()
    {
        return Entries.Select(e => e.PatternType).Distinct();
    }

    /// <summary>
    /// Gets the count of entries for a specific pattern/dan combination.
    /// </summary>
    public int GetEntryCount(string patternType, string danLabel)
    {
        return GetEntries(patternType, danLabel).Count();
    }
}

/// <summary>
/// A single training entry representing a pattern detected in a user's map.
/// </summary>
public class TrainingEntry
{
    /// <summary>
    /// The pattern type name (e.g., "Stream", "Jack", "Chordjack").
    /// </summary>
    [JsonPropertyName("patternType")]
    public string PatternType { get; set; } = string.Empty;

    /// <summary>
    /// The YAVSRG difficulty rating for the map.
    /// </summary>
    [JsonPropertyName("yavsrgRating")]
    public double YavsrgRating { get; set; }

    /// <summary>
    /// Legacy: BPM at which this pattern was detected (for backward compatibility).
    /// </summary>
    [JsonPropertyName("bpm")]
    public double Bpm { get; set; }

    /// <summary>
    /// Legacy: MSD (difficulty) score (for backward compatibility).
    /// </summary>
    [JsonPropertyName("msd")]
    public double Msd { get; set; }

    /// <summary>
    /// The dan label assigned by the user (e.g., "5", "alpha", "kappa").
    /// </summary>
    [JsonPropertyName("danLabel")]
    public string DanLabel { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when this entry was created.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional: Source beatmap path for reference.
    /// </summary>
    [JsonPropertyName("sourcePath")]
    public string? SourcePath { get; set; }
}

/// <summary>
/// Aggregated training data for a specific pattern type within a dan.
/// Used during merge calculations.
/// </summary>
public class TrainingPatternData
{
    /// <summary>
    /// The pattern type name.
    /// </summary>
    public string PatternType { get; set; } = string.Empty;

    /// <summary>
    /// The dan label.
    /// </summary>
    public string DanLabel { get; set; } = string.Empty;

    /// <summary>
    /// Number of training entries contributing to this aggregate.
    /// </summary>
    public int EntryCount { get; set; }

    /// <summary>
    /// Average YAVSRG rating across all training entries.
    /// </summary>
    public double AverageYavsrgRating { get; set; }

    /// <summary>
    /// Minimum YAVSRG rating observed in training entries.
    /// </summary>
    public double MinObservedYavsrgRating { get; set; }

    /// <summary>
    /// Maximum YAVSRG rating observed in training entries.
    /// </summary>
    public double MaxObservedYavsrgRating { get; set; }

    /// <summary>
    /// Legacy: Average BPM (for backward compatibility).
    /// </summary>
    public double AverageBpm { get; set; }

    /// <summary>
    /// Legacy: Average MSD (for backward compatibility).
    /// </summary>
    public double AverageMsd { get; set; }

    /// <summary>
    /// Creates aggregate data from a collection of training entries.
    /// </summary>
    public static TrainingPatternData FromEntries(string patternType, string danLabel, IEnumerable<TrainingEntry> entries)
    {
        var entryList = entries.ToList();
        if (entryList.Count == 0)
        {
            return new TrainingPatternData
            {
                PatternType = patternType,
                DanLabel = danLabel,
                EntryCount = 0
            };
        }

        // Only use entries with YAVSRG rating
        var entriesWithRating = entryList.Where(e => e.YavsrgRating > 0).ToList();
        
        if (entriesWithRating.Count == 0)
        {
            return new TrainingPatternData
            {
                PatternType = patternType,
                DanLabel = danLabel,
                EntryCount = 0
            };
        }

        return new TrainingPatternData
        {
            PatternType = patternType,
            DanLabel = danLabel,
            EntryCount = entriesWithRating.Count,
            AverageYavsrgRating = entriesWithRating.Average(e => e.YavsrgRating),
            MinObservedYavsrgRating = entriesWithRating.Min(e => e.YavsrgRating),
            MaxObservedYavsrgRating = entriesWithRating.Max(e => e.YavsrgRating),
            // Legacy fields for backward compatibility
            AverageBpm = entriesWithRating.Average(e => e.Bpm),
            AverageMsd = entriesWithRating.Average(e => e.Msd)
        };
    }
}

