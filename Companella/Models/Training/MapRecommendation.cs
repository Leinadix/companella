using Companella.Models.Database;

namespace Companella.Models.Training;

/// <summary>
/// Represents a recommended map for the player.
/// </summary>
public class MapRecommendation
{
    /// <summary>
    /// Path to the .osu file.
    /// </summary>
    public string BeatmapPath { get; set; } = string.Empty;

    /// <summary>
    /// The indexed map data.
    /// </summary>
    public IndexedMap? Map { get; set; }

    /// <summary>
    /// Suggested rate if the map needs adjustment (null = play at 1.0x).
    /// </summary>
    public float? SuggestedRate { get; set; }

    /// <summary>
    /// The focus mode this recommendation is for.
    /// </summary>
    public RecommendationFocus Focus { get; set; }

    /// <summary>
    /// Human-readable reasoning for this recommendation.
    /// </summary>
    public string Reasoning { get; set; } = string.Empty;

    /// <summary>
    /// The target skillset for this recommendation (for Skillset and DeficitFixing modes).
    /// </summary>
    public string? TargetSkillset { get; set; }

    /// <summary>
    /// The calculated Map-MMR score.
    /// </summary>
    public double Mmr { get; set; }

    /// <summary>
    /// Expected difficulty relative to player skill (1.0 = at level, >1.0 = harder, <1.0 = easier).
    /// </summary>
    public double RelativeDifficulty { get; set; }

    /// <summary>
    /// Confidence score for this recommendation (0-1).
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Whether a rate change is needed to match target difficulty.
    /// </summary>
    public bool NeedsRateChange => SuggestedRate.HasValue && Math.Abs(SuggestedRate.Value - 1.0f) > 0.05f;

    /// <summary>
    /// Gets the display name for the map.
    /// </summary>
    public string DisplayName => Map?.DisplayName ?? Path.GetFileName(BeatmapPath);

    /// <summary>
    /// Gets a short summary of the recommendation.
    /// </summary>
    public string Summary
    {
        get
        {
            var rate = SuggestedRate.HasValue ? $" @ {SuggestedRate.Value:0.0#}x" : "";
            return $"{DisplayName}{rate}";
        }
    }

    /// <summary>
    /// Gets the effective MSD after rate adjustment.
    /// </summary>
    public float EffectiveMsd
    {
        get
        {
            if (Map == null) return 0;
            if (!SuggestedRate.HasValue) return Map.OverallMsd;
            
            var scores = Map.GetScoresForRate(SuggestedRate.Value);
            return scores?.Overall ?? Map.OverallMsd;
        }
    }
}

/// <summary>
/// A batch of recommendations with metadata.
/// </summary>
public class RecommendationBatch
{
    /// <summary>
    /// The list of recommendations.
    /// </summary>
    public List<MapRecommendation> Recommendations { get; set; } = new();

    /// <summary>
    /// The focus mode these recommendations are for.
    /// </summary>
    public RecommendationFocus Focus { get; set; }

    /// <summary>
    /// The target skillset (if applicable).
    /// </summary>
    public string? TargetSkillset { get; set; }

    /// <summary>
    /// When these recommendations were generated.
    /// </summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Player skill level at time of generation.
    /// </summary>
    public double PlayerSkillLevel { get; set; }

    /// <summary>
    /// Human-readable summary of the recommendations.
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Number of maps in the database that were considered.
    /// </summary>
    public int TotalMapsConsidered { get; set; }
}
