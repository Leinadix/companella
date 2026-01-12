using System.Text.Json.Serialization;

namespace Companella.Models.Training;

/// <summary>
/// Lookup table for converting between dan labels and indices.
/// </summary>
public static class DanLookup
{
    /// <summary>
    /// All dan labels in order from lowest to highest difficulty.
    /// Index 0 = "1", Index 19 = "kappa"
    /// </summary>
    public static readonly string[] Labels = new[]
    {
        "1", "2", "3", "4", "5", "6", "7", "8", "9", "10",
        "alpha", "beta", "gamma", "delta", "epsilon",
        "zeta", "eta", "theta", "iota", "kappa"
    };

    /// <summary>
    /// Gets the dan index (0-19) for a given label.
    /// Returns -1 if not found.
    /// </summary>
    public static int GetIndex(string label)
    {
        for (int i = 0; i < Labels.Length; i++)
        {
            if (Labels[i].Equals(label, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Gets the dan label for a given index.
    /// Returns null if index is out of range.
    /// </summary>
    public static string? GetLabel(int index)
    {
        if (index < 0 || index >= Labels.Length)
            return null;
        return Labels[index];
    }

    /// <summary>
    /// Checks if an index is valid (0-19).
    /// </summary>
    public static bool IsValidIndex(int index) => index >= 0 && index < Labels.Length;

    /// <summary>
    /// Total number of dan levels.
    /// </summary>
    public static int Count => Labels.Length;
}

/// <summary>
/// Root container for dan training data.
/// Stored separately from dans.json to preserve original configuration.
/// </summary>
public class TrainingData
{
    /// <summary>
    /// Schema version for future compatibility.
    /// Version 4 = MSD skillsets + Interlude rating + danIndex as int.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 4;

    /// <summary>
    /// All training entries collected from user maps.
    /// </summary>
    [JsonPropertyName("entries")]
    public List<TrainingEntry> Entries { get; set; } = new();

    /// <summary>
    /// Gets all entries for a specific dan index.
    /// </summary>
    public IEnumerable<TrainingEntry> GetEntriesForDan(int danIndex)
    {
        return Entries.Where(e => e.DanIndex == danIndex);
    }

    /// <summary>
    /// Gets all unique dan indices in the training data.
    /// </summary>
    public IEnumerable<int> GetUniqueDanIndices()
    {
        return Entries.Select(e => e.DanIndex).Distinct();
    }

    /// <summary>
    /// Gets the count of entries for a specific dan index.
    /// </summary>
    public int GetEntryCount(int danIndex)
    {
        return GetEntriesForDan(danIndex).Count();
    }
}

/// <summary>
/// A single training entry representing MSD + Interlude data from a user's map.
/// </summary>
public class TrainingEntry
{
    /// <summary>
    /// All 8 MSD skillset values from MinaCalc for this map.
    /// </summary>
    [JsonPropertyName("msdValues")]
    public MsdSkillsetValues MsdValues { get; set; } = new();

    /// <summary>
    /// The Interlude (YAVSRG) difficulty rating for this map.
    /// </summary>
    [JsonPropertyName("interludeRating")]
    public double InterludeRating { get; set; }

    /// <summary>
    /// The Sunny difficulty rating for this map.
    /// </summary>
    [JsonPropertyName("sunnyRating")]
    public double? SunnyRating { get; set; }

    /// <summary>
    /// The dan index (0-19) assigned by the user.
    /// Use DanLookup to convert to/from label strings.
    /// </summary>
    [JsonPropertyName("danIndex")]
    public int DanIndex { get; set; } = -1;

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

    /// <summary>
    /// Gets the dan label from the index. Returns null if invalid.
    /// </summary>
    [JsonIgnore]
    public string? DanLabel => DanLookup.GetLabel(DanIndex);

    /// <summary>
    /// Checks if this entry has valid MSD values (at least overall > 0).
    /// </summary>
    [JsonIgnore]
    public bool HasValidMsd => MsdValues.Overall > 0;

    /// <summary>
    /// Checks if this entry has a valid Interlude rating.
    /// </summary>
    [JsonIgnore]
    public bool HasValidInterlude => InterludeRating > 0;

    /// <summary>
    /// Checks if this entry has a valid dan index.
    /// </summary>
    [JsonIgnore]
    public bool HasValidDanIndex => DanLookup.IsValidIndex(DanIndex);

    /// <summary>
    /// Checks if this entry has all required data.
    /// </summary>
    [JsonIgnore]
    public bool IsValid => HasValidMsd && HasValidInterlude && HasValidDanIndex;
}

/// <summary>
/// Aggregated training data for a specific dan level.
/// Used during merge calculations to compute average MSD + Interlude values.
/// </summary>
public class TrainingAggregateData
{
    /// <summary>
    /// The dan index (0-19).
    /// </summary>
    public int DanIndex { get; set; } = -1;

    /// <summary>
    /// Number of training entries contributing to this aggregate.
    /// </summary>
    public int EntryCount { get; set; }

    /// <summary>
    /// Average MSD skillset values across all training entries.
    /// </summary>
    public MsdSkillsetValues AverageMsdValues { get; set; } = new();

    /// <summary>
    /// Average Interlude rating across all training entries.
    /// </summary>
    public double AverageInterludeRating { get; set; }

    /// <summary>
    /// Minimum Interlude rating observed in training entries.
    /// </summary>
    public double MinInterludeRating { get; set; }

    /// <summary>
    /// Maximum Interlude rating observed in training entries.
    /// </summary>
    public double MaxInterludeRating { get; set; }

    /// <summary>
    /// Gets the dan label from the index.
    /// </summary>
    [JsonIgnore]
    public string? DanLabel => DanLookup.GetLabel(DanIndex);

    /// <summary>
    /// Creates aggregate data from a collection of training entries.
    /// </summary>
    public static TrainingAggregateData FromEntries(int danIndex, IEnumerable<TrainingEntry> entries)
    {
        var entryList = entries.Where(e => e.IsValid).ToList();
        
        if (entryList.Count == 0)
        {
            return new TrainingAggregateData
            {
                DanIndex = danIndex,
                EntryCount = 0
            };
        }

        // Calculate average for each MSD skillset
        var avgMsd = new MsdSkillsetValues
        {
            Overall = entryList.Average(e => e.MsdValues.Overall),
            Stream = entryList.Average(e => e.MsdValues.Stream),
            Jumpstream = entryList.Average(e => e.MsdValues.Jumpstream),
            Handstream = entryList.Average(e => e.MsdValues.Handstream),
            Stamina = entryList.Average(e => e.MsdValues.Stamina),
            Jackspeed = entryList.Average(e => e.MsdValues.Jackspeed),
            Chordjack = entryList.Average(e => e.MsdValues.Chordjack),
            Technical = entryList.Average(e => e.MsdValues.Technical)
        };

        return new TrainingAggregateData
        {
            DanIndex = danIndex,
            EntryCount = entryList.Count,
            AverageMsdValues = avgMsd,
            AverageInterludeRating = entryList.Average(e => e.InterludeRating),
            MinInterludeRating = entryList.Min(e => e.InterludeRating),
            MaxInterludeRating = entryList.Max(e => e.InterludeRating)
        };
    }
}
