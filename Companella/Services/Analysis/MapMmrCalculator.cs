using Companella.Models.Database;
using Companella.Models.Session;
using Companella.Services.Database;

namespace Companella.Services.Analysis;

/// <summary>
/// Service for calculating Map-MMR (map matchmaking rating).
/// Uses a hybrid approach combining map MSD with player performance.
/// </summary>
public class MapMmrCalculator
{
    private readonly MapsDatabaseService _mapsDatabase;

    /// <summary>
    /// Weight for base MSD in MMR calculation (0-1).
    /// </summary>
    private const double BaseMsdWeight = 0.6;

    /// <summary>
    /// Weight for performance adjustment in MMR calculation (0-1).
    /// </summary>
    private const double PerformanceWeight = 0.4;

    /// <summary>
    /// Creates a new MapMmrCalculator.
    /// </summary>
    public MapMmrCalculator(MapsDatabaseService mapsDatabase)
    {
        _mapsDatabase = mapsDatabase;
    }

    /// <summary>
    /// Calculates the MMR for a map relative to player skill.
    /// </summary>
    /// <param name="map">The indexed map.</param>
    /// <param name="playerTrends">Player skill trends.</param>
    /// <param name="targetSkillset">Optional specific skillset to calculate for.</param>
    /// <returns>The calculated MMR and relative difficulty.</returns>
    public MapMmrResult CalculateMapMmr(IndexedMap map, SkillsTrendResult playerTrends, string? targetSkillset = null)
    {
        var result = new MapMmrResult
        {
            Map = map
        };

        if (map.MsdScores.Count == 0)
        {
            // No MSD data, use overall MSD as a fallback
            result.Mmr = map.OverallMsd;
            result.RelativeDifficulty = 1.0;
            return result;
        }

        // Get the relevant skillset MSD
        var skillset = targetSkillset ?? map.DominantSkillset;
        var msdAt1x = map.GetScoresForRate(1.0f);
        
        double baseMsd;
        if (msdAt1x != null && !string.IsNullOrEmpty(skillset))
        {
            baseMsd = msdAt1x.GetSkillsetValue(skillset);
        }
        else
        {
            baseMsd = map.OverallMsd;
        }

        // Get player's skill level for this skillset
        var playerSkill = GetPlayerSkillForSkillset(playerTrends, skillset);
        
        // Calculate performance adjustment based on player's history with this map
        var performanceAdjustment = CalculatePerformanceAdjustment(map, playerSkill);

        // Calculate hybrid MMR
        // MMR = baseMSD * baseMsdWeight + adjustedMSD * performanceWeight
        var adjustedMsd = baseMsd + performanceAdjustment;
        result.Mmr = baseMsd * BaseMsdWeight + adjustedMsd * PerformanceWeight;

        // Calculate relative difficulty (how hard this map is compared to player skill)
        if (playerSkill > 0)
        {
            result.RelativeDifficulty = result.Mmr / playerSkill;
        }
        else
        {
            result.RelativeDifficulty = 1.0;
        }

        // Calculate confidence based on available data
        result.Confidence = CalculateConfidence(map, playerTrends);

        return result;
    }

    /// <summary>
    /// Ranks a list of maps by their MMR relative to player skill.
    /// </summary>
    /// <param name="maps">Maps to rank.</param>
    /// <param name="trends">Player skill trends.</param>
    /// <param name="targetSkillset">Optional skillset to rank by.</param>
    /// <returns>Maps with MMR data, sorted by relative difficulty.</returns>
    public List<MapMmrResult> RankMaps(List<IndexedMap> maps, SkillsTrendResult trends, string? targetSkillset = null)
    {
        var results = new List<MapMmrResult>();

        foreach (var map in maps)
        {
            var mmrResult = CalculateMapMmr(map, trends, targetSkillset);
            results.Add(mmrResult);
        }

        // Sort by relative difficulty (closest to 1.0 = best match)
        return results.OrderBy(r => Math.Abs(r.RelativeDifficulty - 1.0)).ToList();
    }

    /// <summary>
    /// Finds maps in an optimal difficulty range for the player.
    /// </summary>
    /// <param name="trends">Player skill trends.</param>
    /// <param name="targetDifficultyRatio">Target relative difficulty (1.0 = at level, 1.1 = 10% harder).</param>
    /// <param name="tolerance">Acceptable deviation from target (e.g., 0.1 for +/-10%).</param>
    /// <param name="skillset">Optional skillset filter.</param>
    /// <param name="limit">Maximum maps to return.</param>
    public List<MapMmrResult> FindMapsInOptimalRange(
        SkillsTrendResult trends,
        double targetDifficultyRatio = 1.0,
        double tolerance = 0.15,
        string? skillset = null,
        int limit = 20)
    {
        // Calculate target MSD range based on player skill
        var playerSkill = skillset != null 
            ? GetPlayerSkillForSkillset(trends, skillset)
            : trends.OverallSkillLevel;

        if (playerSkill <= 0)
        {
            // No skill data, return empty
            return new List<MapMmrResult>();
        }

        var targetMsd = playerSkill * targetDifficultyRatio;
        var minMsd = (float)(targetMsd * (1 - tolerance));
        var maxMsd = (float)(targetMsd * (1 + tolerance));

        // Search for maps in the range
        var criteria = new MapSearchCriteria
        {
            MinMsd = minMsd,
            MaxMsd = maxMsd,
            Skillset = skillset,
            KeyCount = 4,
            Limit = limit * 2, // Get more to filter
            OrderBy = MapSearchOrderBy.Random
        };

        var maps = _mapsDatabase.SearchMaps(criteria);
        
        // Calculate MMR for each and filter by relative difficulty
        var results = new List<MapMmrResult>();
        foreach (var map in maps)
        {
            var mmrResult = CalculateMapMmr(map, trends, skillset);
            
            // Check if within tolerance
            var minRatio = targetDifficultyRatio - tolerance;
            var maxRatio = targetDifficultyRatio + tolerance;
            
            if (mmrResult.RelativeDifficulty >= minRatio && mmrResult.RelativeDifficulty <= maxRatio)
            {
                results.Add(mmrResult);
            }
        }

        return results
            .OrderBy(r => Math.Abs(r.RelativeDifficulty - targetDifficultyRatio))
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Gets the player's skill level for a specific skillset.
    /// </summary>
    private double GetPlayerSkillForSkillset(SkillsTrendResult trends, string? skillset)
    {
        if (string.IsNullOrEmpty(skillset))
        {
            return trends.OverallSkillLevel;
        }

        var key = skillset.ToLowerInvariant();
        if (trends.CurrentSkillLevels.TryGetValue(key, out var level))
        {
            return level;
        }

        return trends.OverallSkillLevel;
    }

    /// <summary>
    /// Calculates performance adjustment based on player's history with the map.
    /// </summary>
    private double CalculatePerformanceAdjustment(IndexedMap map, double playerSkill)
    {
        if (map.PlayerStats.Count == 0)
        {
            // No play history - no adjustment
            return 0;
        }

        // Get average accuracy on this map
        var avgAccuracy = map.PlayerStats.Average(s => s.Accuracy);
        
        // Calculate adjustment based on accuracy
        // If accuracy is high (>95%), map might be slightly easier than MSD suggests
        // If accuracy is low (<90%), map might be harder than MSD suggests
        if (avgAccuracy > 95)
        {
            // Slightly reduce effective difficulty
            return -0.5 * (avgAccuracy - 95) / 5;
        }
        else if (avgAccuracy < 90)
        {
            // Slightly increase effective difficulty
            return 0.5 * (90 - avgAccuracy) / 10;
        }

        return 0;
    }

    /// <summary>
    /// Calculates confidence in the MMR calculation.
    /// </summary>
    private double CalculateConfidence(IndexedMap map, SkillsTrendResult trends)
    {
        double confidence = 0;

        // More MSD scores = more confidence
        if (map.MsdScores.Count > 0)
        {
            confidence += 0.4;
        }

        // More player history = more confidence
        if (map.PlayCount > 0)
        {
            confidence += Math.Min(0.3, map.PlayCount * 0.1);
        }

        // More trend data = more confidence
        if (trends.TotalPlays >= 10)
        {
            confidence += 0.3;
        }
        else if (trends.TotalPlays >= 5)
        {
            confidence += 0.15;
        }

        return Math.Min(1.0, confidence);
    }

    /// <summary>
    /// Calculates the suggested rate to play a map at to match target difficulty.
    /// </summary>
    /// <param name="map">The map.</param>
    /// <param name="targetMsd">Target MSD to achieve.</param>
    /// <returns>Suggested rate (0.7 to 2.0) or null if no good rate exists.</returns>
    public float? GetSuggestedRate(IndexedMap map, float targetMsd)
    {
        if (map.MsdScores.Count == 0)
        {
            return null;
        }

        // Find the rate that gives closest MSD to target
        float? bestRate = null;
        float bestDiff = float.MaxValue;

        foreach (var score in map.MsdScores)
        {
            var diff = Math.Abs(score.Overall - targetMsd);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                bestRate = score.Rate;
            }
        }

        // If best rate gives MSD within 2 of target, use it
        if (bestRate.HasValue && bestDiff <= 2.0f)
        {
            return bestRate;
        }

        // Interpolate between rates if needed
        var sortedScores = map.MsdScores.OrderBy(s => s.Rate).ToList();
        for (int i = 0; i < sortedScores.Count - 1; i++)
        {
            var lower = sortedScores[i];
            var upper = sortedScores[i + 1];

            if (targetMsd >= lower.Overall && targetMsd <= upper.Overall)
            {
                // Linear interpolation
                var t = (targetMsd - lower.Overall) / (upper.Overall - lower.Overall);
                var interpolatedRate = lower.Rate + t * (upper.Rate - lower.Rate);
                return (float)Math.Round(interpolatedRate, 1);
            }
        }

        return bestRate;
    }
}

/// <summary>
/// Result of MMR calculation for a map.
/// </summary>
public class MapMmrResult
{
    /// <summary>
    /// The indexed map.
    /// </summary>
    public IndexedMap Map { get; set; } = null!;

    /// <summary>
    /// The calculated MMR value.
    /// </summary>
    public double Mmr { get; set; }

    /// <summary>
    /// Relative difficulty compared to player skill (1.0 = at level).
    /// </summary>
    public double RelativeDifficulty { get; set; }

    /// <summary>
    /// Confidence in the calculation (0-1).
    /// </summary>
    public double Confidence { get; set; }
}
