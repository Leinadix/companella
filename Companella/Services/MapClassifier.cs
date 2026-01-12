using Companella.Models.Beatmap;
using Companella.Models.Difficulty;
using Companella.Models.Training;

namespace Companella.Services;

/// <summary>
/// Service for classifying maps into difficulty levels based on MSD and pattern analysis.
/// This is a PREPARATION class - full classification logic will be implemented later.
/// 
/// Difficulty levels: 1-10 (numeric), then alpha through kappa (Greek letters).
/// Level progression: 1 (easiest) -> 10 -> alpha -> beta -> ... -> kappa (hardest)
/// </summary>
public class MapClassifier
{
    /// <summary>
    /// Configuration for classification thresholds.
    /// Will be expanded with actual thresholds in future implementation.
    /// </summary>
    public Dictionary<string, double> ClassificationRules { get; set; } = new()
    {
        // MSD thresholds (placeholder values for future implementation)
        { "msd_level_1_max", 5.0 },
        { "msd_level_2_max", 8.0 },
        { "msd_level_3_max", 11.0 },
        { "msd_level_4_max", 14.0 },
        { "msd_level_5_max", 17.0 },
        { "msd_level_6_max", 20.0 },
        { "msd_level_7_max", 23.0 },
        { "msd_level_8_max", 26.0 },
        { "msd_level_9_max", 29.0 },
        { "msd_level_10_max", 32.0 },
        // Greek levels start at higher MSD
        { "msd_alpha_max", 34.0 },
        { "msd_beta_max", 36.0 },
        { "msd_gamma_max", 38.0 },
        { "msd_delta_max", 40.0 },
        { "msd_epsilon_max", 42.0 },
        { "msd_zeta_max", 44.0 },
        { "msd_eta_max", 46.0 },
        { "msd_theta_max", 48.0 },
        { "msd_iota_max", 50.0 },
        // kappa = anything above 50
        
        // BPM weight multipliers (placeholder)
        { "bpm_weight_low", 0.8 },   // Below 120 BPM
        { "bpm_weight_mid", 1.0 },   // 120-180 BPM
        { "bpm_weight_high", 1.2 },  // 180-220 BPM
        { "bpm_weight_extreme", 1.5 } // Above 220 BPM
    };

    /// <summary>
    /// Creates comparison data from MSD result, pattern analysis, and OsuFile.
    /// </summary>
    public MapComparisonData CreateComparisonData(SingleRateMsdResult? msdResult, PatternAnalysisResult? patterns, OsuFile? osuFile)
    {
        return new MapComparisonData
        {
            MsdScores = msdResult?.Scores,
            PatternResult = patterns,
            OsuFile = osuFile
        };
    }

    /// <summary>
    /// Creates comparison data from full MSD result (uses 1.0x rate).
    /// </summary>
    public MapComparisonData CreateComparisonData(MsdResult? msdResult, PatternAnalysisResult? patterns, OsuFile? osuFile)
    {
        return new MapComparisonData
        {
            MsdScores = msdResult?.GetScoresForRate(1.0f),
            PatternResult = patterns,
            OsuFile = osuFile
        };
    }

    /// <summary>
    /// Extracts pattern-related metrics from pattern analysis.
    /// </summary>
    public PatternMetrics GetPatternMetrics(PatternAnalysisResult? patterns)
    {
        if (patterns == null || !patterns.Success)
        {
            return new PatternMetrics();
        }

        var metrics = new PatternMetrics
        {
            TotalPatterns = patterns.TotalPatterns,
            TotalNotes = patterns.TotalNotes,
            PatternCounts = new Dictionary<PatternType, int>(),
            PatternBpmRanges = new Dictionary<PatternType, (double Min, double Max)>()
        };

        foreach (var kvp in patterns.Patterns)
        {
            metrics.PatternCounts[kvp.Key] = kvp.Value.Count;

            if (kvp.Value.Count > 0)
            {
                var bpms = kvp.Value.Select(p => p.Bpm).ToList();
                metrics.PatternBpmRanges[kvp.Key] = (bpms.Min(), bpms.Max());

                // Track peak BPM
                var maxBpm = bpms.Max();
                if (maxBpm > metrics.PeakBpm)
                {
                    metrics.PeakBpm = maxBpm;
                    metrics.PeakBpmPattern = kvp.Key;
                }
            }
        }

        // Calculate average BPM
        var allBpms = patterns.Patterns.Values
            .SelectMany(p => p)
            .Select(p => p.Bpm)
            .ToList();

        if (allBpms.Count > 0)
        {
            metrics.AverageBpm = allBpms.Average();
        }

        // Find dominant pattern
        if (metrics.PatternCounts.Count > 0)
        {
            var dominant = metrics.PatternCounts.MaxBy(p => p.Value);
            metrics.DominantPattern = dominant.Key;
            metrics.DominantPatternCount = dominant.Value;
        }

        // Calculate complexity (variety of patterns)
        metrics.PatternVariety = metrics.PatternCounts.Count(p => p.Value > 0);

        return metrics;
    }

    /// <summary>
    /// Extracts MSD-related metrics from skillset scores.
    /// </summary>
    public MsdMetrics GetMsdMetrics(SkillsetScores? scores)
    {
        if (scores == null)
        {
            return new MsdMetrics();
        }

        var skillsets = new Dictionary<string, float>
        {
            { "stream", scores.Stream },
            { "jumpstream", scores.Jumpstream },
            { "handstream", scores.Handstream },
            { "stamina", scores.Stamina },
            { "jackspeed", scores.Jackspeed },
            { "chordjack", scores.Chordjack },
            { "technical", scores.Technical }
        };

        var maxSkillset = skillsets.MaxBy(s => s.Value);
        var minSkillset = skillsets.MinBy(s => s.Value);

        return new MsdMetrics
        {
            Overall = scores.Overall,
            DominantSkillset = maxSkillset.Key,
            DominantSkillsetValue = maxSkillset.Value,
            WeakestSkillset = minSkillset.Key,
            WeakestSkillsetValue = minSkillset.Value,
            Spread = maxSkillset.Value - minSkillset.Value,
            SkillsetValues = skillsets,
            IsSpecialized = (maxSkillset.Value - minSkillset.Value) > 5.0f
        };
    }

    /// <summary>
    /// Analyzes BPM distribution and ranges from patterns.
    /// </summary>
    public BpmMetrics GetBpmMetrics(PatternAnalysisResult? patterns)
    {
        if (patterns == null || !patterns.Success)
        {
            return new BpmMetrics();
        }

        var allPatterns = patterns.Patterns.Values.SelectMany(p => p).ToList();

        if (allPatterns.Count == 0)
        {
            return new BpmMetrics();
        }

        var bpms = allPatterns.Select(p => p.Bpm).ToList();

        return new BpmMetrics
        {
            MinBpm = bpms.Min(),
            MaxBpm = bpms.Max(),
            AverageBpm = bpms.Average(),
            BpmRange = bpms.Max() - bpms.Min(),
            IsVariableBpm = (bpms.Max() - bpms.Min()) > 10,
            TotalPatternInstances = allPatterns.Count,
            BpmDistribution = GetBpmDistribution(bpms)
        };
    }

    /// <summary>
    /// Gets BPM distribution in buckets.
    /// </summary>
    private Dictionary<string, int> GetBpmDistribution(List<double> bpms)
    {
        var distribution = new Dictionary<string, int>
        {
            { "0-100", 0 },
            { "100-140", 0 },
            { "140-180", 0 },
            { "180-220", 0 },
            { "220-260", 0 },
            { "260+", 0 }
        };

        foreach (var bpm in bpms)
        {
            if (bpm < 100) distribution["0-100"]++;
            else if (bpm < 140) distribution["100-140"]++;
            else if (bpm < 180) distribution["140-180"]++;
            else if (bpm < 220) distribution["180-220"]++;
            else if (bpm < 260) distribution["220-260"]++;
            else distribution["260+"]++;
        }

        return distribution;
    }

    /// <summary>
    /// Classifies a map into a difficulty level.
    /// PLACEHOLDER - Full classification logic to be implemented later.
    /// Currently returns a basic classification based on MSD only.
    /// </summary>
    public MapClassification ClassifyMap(MapComparisonData data)
    {
        var msdMetrics = GetMsdMetrics(data.MsdScores);
        var patternMetrics = GetPatternMetrics(data.PatternResult);
        var bpmMetrics = GetBpmMetrics(data.PatternResult);

        // Basic classification based on MSD (placeholder logic)
        var levelIndex = CalculateLevelIndex(msdMetrics.Overall, bpmMetrics.MaxBpm, patternMetrics.PatternVariety);

        return new MapClassification
        {
            LevelIndex = levelIndex,
            Confidence = 0.5, // Placeholder confidence
            Reason = $"Based on MSD {msdMetrics.Overall:F1} and {patternMetrics.TotalPatterns} patterns",
            DominantSkillset = msdMetrics.DominantSkillset,
            DominantPattern = patternMetrics.DominantPattern,
            PeakBpm = bpmMetrics.MaxBpm,
            Factors = new List<string>
            {
                $"MSD: {msdMetrics.Overall:F1}",
                $"Peak BPM: {bpmMetrics.MaxBpm:F0}",
                $"Dominant: {msdMetrics.DominantSkillset}",
                $"Pattern variety: {patternMetrics.PatternVariety}"
            }
        };
    }

    /// <summary>
    /// Calculates the level index based on metrics.
    /// PLACEHOLDER - Will be refined with actual classification logic.
    /// </summary>
    private int CalculateLevelIndex(float overallMsd, double peakBpm, int patternVariety)
    {
        // Very basic placeholder logic
        // Real implementation will consider pattern types, BPM weights, etc.

        // Base level from MSD
        int level;
        if (overallMsd < 5) level = 1;
        else if (overallMsd < 8) level = 2;
        else if (overallMsd < 11) level = 3;
        else if (overallMsd < 14) level = 4;
        else if (overallMsd < 17) level = 5;
        else if (overallMsd < 20) level = 6;
        else if (overallMsd < 23) level = 7;
        else if (overallMsd < 26) level = 8;
        else if (overallMsd < 29) level = 9;
        else if (overallMsd < 32) level = 10;
        else if (overallMsd < 34) level = 11; // alpha
        else if (overallMsd < 36) level = 12; // beta
        else if (overallMsd < 38) level = 13; // gamma
        else if (overallMsd < 40) level = 14; // delta
        else if (overallMsd < 42) level = 15; // epsilon
        else if (overallMsd < 44) level = 16; // zeta
        else if (overallMsd < 46) level = 17; // eta
        else if (overallMsd < 48) level = 18; // theta
        else if (overallMsd < 50) level = 19; // iota
        else level = 20; // kappa (max)

        // Adjust for BPM (placeholder - would need more sophisticated logic)
        if (peakBpm > 220 && level < 20)
        {
            level = Math.Min(level + 1, 20);
        }

        return Math.Clamp(level, MapClassification.MinLevel, MapClassification.MaxLevel);
    }

    /// <summary>
    /// Gets the level label for a given index.
    /// Convenience method wrapping MapClassification.GetLevelLabel.
    /// </summary>
    public static string GetLevelLabel(int levelIndex)
    {
        return MapClassification.GetLevelLabel(levelIndex);
    }

    /// <summary>
    /// Gets all available level labels.
    /// </summary>
    public static IReadOnlyList<string> GetAllLevelLabels()
    {
        return MapClassification.GetAllLevelLabels();
    }
}

/// <summary>
/// Metrics extracted from pattern analysis.
/// </summary>
public class PatternMetrics
{
    public int TotalPatterns { get; set; }
    public int TotalNotes { get; set; }
    public Dictionary<PatternType, int> PatternCounts { get; set; } = new();
    public Dictionary<PatternType, (double Min, double Max)> PatternBpmRanges { get; set; } = new();
    public double PeakBpm { get; set; }
    public PatternType? PeakBpmPattern { get; set; }
    public double AverageBpm { get; set; }
    public PatternType? DominantPattern { get; set; }
    public int DominantPatternCount { get; set; }
    public int PatternVariety { get; set; }
}

/// <summary>
/// Metrics extracted from MSD scores.
/// </summary>
public class MsdMetrics
{
    public float Overall { get; set; }
    public string DominantSkillset { get; set; } = string.Empty;
    public float DominantSkillsetValue { get; set; }
    public string WeakestSkillset { get; set; } = string.Empty;
    public float WeakestSkillsetValue { get; set; }
    public float Spread { get; set; }
    public Dictionary<string, float> SkillsetValues { get; set; } = new();
    public bool IsSpecialized { get; set; }
}

/// <summary>
/// Metrics extracted from BPM analysis.
/// </summary>
public class BpmMetrics
{
    public double MinBpm { get; set; }
    public double MaxBpm { get; set; }
    public double AverageBpm { get; set; }
    public double BpmRange { get; set; }
    public bool IsVariableBpm { get; set; }
    public int TotalPatternInstances { get; set; }
    public Dictionary<string, int> BpmDistribution { get; set; } = new();
}
