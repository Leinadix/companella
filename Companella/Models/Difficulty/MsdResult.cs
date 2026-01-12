using System.Text.Json.Serialization;

namespace Companella.Models.Difficulty;

/// <summary>
/// Represents the full MSD analysis result from msd-calculator.
/// </summary>
public class MsdResult
{
    /// <summary>
    /// Path to the analyzed beatmap file.
    /// </summary>
    [JsonPropertyName("beatmap_path")]
    public string BeatmapPath { get; set; } = string.Empty;

    /// <summary>
    /// MinaCalc version used for calculation.
    /// </summary>
    [JsonPropertyName("minacalc_version")]
    public int MinaCalcVersion { get; set; }

    /// <summary>
    /// MSD scores for all rates (0.7x to 2.0x).
    /// </summary>
    [JsonPropertyName("rates")]
    public List<RateEntry> Rates { get; set; } = new();

    /// <summary>
    /// The dominant skillset at 1.0x rate (highest non-overall score).
    /// </summary>
    [JsonPropertyName("dominant_skillset")]
    public string DominantSkillset { get; set; } = string.Empty;

    /// <summary>
    /// Overall difficulty at 1.0x rate.
    /// </summary>
    [JsonPropertyName("difficulty_1x")]
    public float Difficulty1x { get; set; }

    /// <summary>
    /// Gets the scores for a specific rate.
    /// </summary>
    /// <param name="rate">Rate value (0.7 to 2.0)</param>
    /// <returns>Skillset scores or null if rate not found.</returns>
    public SkillsetScores? GetScoresForRate(float rate)
    {
        return Rates.Find(r => Math.Abs(r.Rate - rate) < 0.05f)?.Scores;
    }
}

/// <summary>
/// Represents MSD scores for a single rate.
/// </summary>
public class RateEntry
{
    /// <summary>
    /// The music rate (0.7 to 2.0).
    /// </summary>
    [JsonPropertyName("rate")]
    public float Rate { get; set; }

    /// <summary>
    /// Skillset scores at this rate.
    /// </summary>
    [JsonPropertyName("scores")]
    public SkillsetScores Scores { get; set; } = new();
}

/// <summary>
/// Represents skillset difficulty scores from MinaCalc.
/// </summary>
public class SkillsetScores
{
    /// <summary>
    /// Overall difficulty rating.
    /// </summary>
    [JsonPropertyName("overall")]
    public float Overall { get; set; }

    /// <summary>
    /// Stream pattern difficulty.
    /// </summary>
    [JsonPropertyName("stream")]
    public float Stream { get; set; }

    /// <summary>
    /// Jumpstream pattern difficulty.
    /// </summary>
    [JsonPropertyName("jumpstream")]
    public float Jumpstream { get; set; }

    /// <summary>
    /// Handstream pattern difficulty.
    /// </summary>
    [JsonPropertyName("handstream")]
    public float Handstream { get; set; }

    /// <summary>
    /// Stamina requirement difficulty.
    /// </summary>
    [JsonPropertyName("stamina")]
    public float Stamina { get; set; }

    /// <summary>
    /// Jackspeed pattern difficulty.
    /// </summary>
    [JsonPropertyName("jackspeed")]
    public float Jackspeed { get; set; }

    /// <summary>
    /// Chordjack pattern difficulty.
    /// </summary>
    [JsonPropertyName("chordjack")]
    public float Chordjack { get; set; }

    /// <summary>
    /// Technical complexity difficulty.
    /// </summary>
    [JsonPropertyName("technical")]
    public float Technical { get; set; }

    /// <summary>
    /// Gets all skillset values as a dictionary for easy iteration.
    /// </summary>
    public Dictionary<string, float> ToDictionary()
    {
        return new Dictionary<string, float>
        {
            { "overall", Overall },
            { "stream", Stream },
            { "jumpstream", Jumpstream },
            { "handstream", Handstream },
            { "stamina", Stamina },
            { "jackspeed", Jackspeed },
            { "chordjack", Chordjack },
            { "technical", Technical }
        };
    }

    /// <summary>
    /// Gets the highest skillset (excluding overall) and its value.
    /// </summary>
    public (string Name, float Value) GetDominantSkillset()
    {
        var skillsets = new (string Name, float Value)[]
        {
            ("stream", Stream),
            ("jumpstream", Jumpstream),
            ("handstream", Handstream),
            ("stamina", Stamina),
            ("jackspeed", Jackspeed),
            ("chordjack", Chordjack),
            ("technical", Technical)
        };

        var max = skillsets.MaxBy(s => s.Value);
        return max;
    }
}

/// <summary>
/// Represents a single-rate MSD result (when --rate is specified).
/// </summary>
public class SingleRateMsdResult
{
    /// <summary>
    /// Path to the analyzed beatmap file.
    /// </summary>
    [JsonPropertyName("beatmap_path")]
    public string BeatmapPath { get; set; } = string.Empty;

    /// <summary>
    /// MinaCalc version used for calculation.
    /// </summary>
    [JsonPropertyName("minacalc_version")]
    public int MinaCalcVersion { get; set; }

    /// <summary>
    /// The music rate.
    /// </summary>
    [JsonPropertyName("rate")]
    public float Rate { get; set; }

    /// <summary>
    /// Skillset scores at this rate.
    /// </summary>
    [JsonPropertyName("scores")]
    public SkillsetScores Scores { get; set; } = new();

    /// <summary>
    /// The dominant skillset at this rate.
    /// </summary>
    [JsonPropertyName("dominant_skillset")]
    public string DominantSkillset { get; set; } = string.Empty;
}
