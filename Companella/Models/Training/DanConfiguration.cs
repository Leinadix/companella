using System.Text.Json.Serialization;
using Companella.Models.Beatmap;

namespace Companella.Models.Training;

/// <summary>
/// Root configuration for dans (difficulty levels).
/// Uses YAVSRG difficulty ratings per pattern type.
/// </summary>
public class DanConfiguration
{
    /// <summary>
    /// List of all dan definitions, ordered from lowest to highest.
    /// </summary>
    [JsonPropertyName("dans")]
    public List<DanDefinition> Dans { get; set; } = new();

    /// <summary>
    /// Schema version for future compatibility.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 2;

    /// <summary>
    /// Creates a default configuration with placeholder YAVSRG rating values.
    /// </summary>
    public static DanConfiguration CreateDefault()
    {
        var config = new DanConfiguration { Version = 2 };

        // Create all 20 levels (1-10 numeric, then Greek letter names)
        var labels = new[]
        {
            "1", "2", "3", "4", "5", "6", "7", "8", "9", "10",
            "alpha", "beta", "gamma", "delta", "epsilon", 
            "zeta", "eta", "theta", "iota", "kappa"
        };

        // Pattern types to include
        var patternTypes = Enum.GetValues<PatternType>();

        for (int i = 0; i < labels.Length; i++)
        {
            var dan = new DanDefinition
            {
                Label = labels[i],
                Patterns = new Dictionary<string, double>()
            };

            // Create placeholder YAVSRG ratings for each pattern type
            // Scale from ~3.0 (dan 1) to ~12.0+ (kappa)
            // Different patterns have slightly different scaling
            foreach (var patternType in patternTypes)
            {
                // Base rating scales with dan level
                var baseRating = 3.0 + (i * 0.5);
                
                // Adjust slightly by pattern type (some patterns are harder at same rating)
                var adjustment = patternType switch
                {
                    PatternType.Jack => 0.2,        // Jacks are slightly harder
                    PatternType.Chordjack => 0.3,   // Chordjacks are harder
                    PatternType.Handstream => 0.1,  // Handstream slightly harder
                    PatternType.Jumptrill => 0.2,   // Jumptrills harder
                    PatternType.Stream => -0.1,     // Streams slightly easier
                    PatternType.Trill => -0.2,      // Trills easier
                    _ => 0.0
                };

                dan.Patterns[patternType.ToString()] = Math.Round(baseRating + adjustment, 2);
            }

            config.Dans.Add(dan);
        }

        return config;
    }
}

/// <summary>
/// Definition of a single dan (difficulty level).
/// </summary>
public class DanDefinition
{
    /// <summary>
    /// The label for this dan (1-10 or Greek letter).
    /// </summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// YAVSRG difficulty ratings for each pattern type.
    /// Key is the pattern type name (e.g., "Stream", "Jack").
    /// Value is the target YAVSRG rating for this dan level.
    /// </summary>
    [JsonPropertyName("patterns")]
    public Dictionary<string, double> Patterns { get; set; } = new();

    /// <summary>
    /// Gets the display name for this dan.
    /// </summary>
    [JsonIgnore]
    public string DisplayName => Label;

    /// <summary>
    /// Gets the low variant display name.
    /// </summary>
    [JsonIgnore]
    public string LowDisplayName => $"{Label}-";

    /// <summary>
    /// Gets the high variant display name.
    /// </summary>
    [JsonIgnore]
    public string HighDisplayName => $"{Label}+";
}

/// <summary>
/// Result of classifying a map against dans using YAVSRG difficulty.
/// </summary>
public class DanClassificationResult
{
    /// <summary>
    /// The matched dan label.
    /// </summary>
    public string Label { get; set; } = "?";

    /// <summary>
    /// Variant: null for mid-range, "Low" or "High" for edge cases.
    /// Determined by comparing to adjacent dan thresholds.
    /// </summary>
    public string? Variant { get; set; }

    /// <summary>
    /// Gets the full display name including variant.
    /// Uses --/-/+/++ notation for variants.
    /// </summary>
    public string DisplayName => Variant switch
    {
        "--" => $"{Label}--",
        "-" => $"{Label}-",
        "+" => $"{Label}+",
        "++" => $"{Label}++",
        _ => Label
    };

    /// <summary>
    /// Confidence score (0.0 - 1.0).
    /// Higher when the rating is closer to the dan's target rating.
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// The dominant pattern type that influenced classification.
    /// </summary>
    public string? DominantPattern { get; set; }

    /// <summary>
    /// The YAVSRG difficulty rating used for classification.
    /// </summary>
    public double YavsrgRating { get; set; }

    /// <summary>
    /// The target rating for the matched dan's pattern.
    /// </summary>
    public double TargetRating { get; set; }

    /// <summary>
    /// Index of the matched dan (0-19, or -1 if unknown).
    /// </summary>
    public int DanIndex { get; set; } = -1;
}
