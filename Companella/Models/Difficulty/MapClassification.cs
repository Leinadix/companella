using Companella.Models.Beatmap;

namespace Companella.Models.Difficulty;

/// <summary>
/// Represents a map difficulty classification/level.
/// Uses numeric labels 1-10, then Greek letters alpha through kappa.
/// </summary>
public class MapClassification
{
    /// <summary>
    /// Greek letter names used for levels beyond 10.
    /// Alpha (11) through Kappa (20) - kappa is the final/hardest level.
    /// </summary>
    private static readonly string[] GreekLetters = 
    {
        "alpha",   // 11
        "beta",    // 12
        "gamma",   // 13
        "delta",   // 14
        "epsilon", // 15
        "zeta",    // 16
        "eta",     // 17
        "theta",   // 18
        "iota",    // 19
        "kappa"    // 20 - FINAL
    };

    /// <summary>
    /// The difficulty level index (1-20).
    /// 1-10 are numeric, 11-20 are Greek letters.
    /// </summary>
    public int LevelIndex { get; set; }

    /// <summary>
    /// The display label for the level.
    /// "1" through "10" for numeric levels.
    /// Greek letters for levels 11-20.
    /// </summary>
    public string Level => GetLevelLabel(LevelIndex);

    /// <summary>
    /// Confidence score for the classification (0.0 - 1.0).
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Primary reason for this classification.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Secondary factors that influenced the classification.
    /// </summary>
    public List<string> Factors { get; set; } = new();

    /// <summary>
    /// The dominant skillset that influenced this classification.
    /// </summary>
    public string DominantSkillset { get; set; } = string.Empty;

    /// <summary>
    /// The dominant pattern type that influenced this classification.
    /// </summary>
    public PatternType? DominantPattern { get; set; }

    /// <summary>
    /// The peak BPM of patterns in the map.
    /// </summary>
    public double PeakBpm { get; set; }

    /// <summary>
    /// Gets the level label for a given index.
    /// </summary>
    /// <param name="levelIndex">Level index (1-20).</param>
    /// <returns>Level label string.</returns>
    public static string GetLevelLabel(int levelIndex)
    {
        if (levelIndex < 1)
            return "?";
        if (levelIndex <= 10)
            return levelIndex.ToString();
        if (levelIndex <= 20)
            return GreekLetters[levelIndex - 11];
        // Beyond kappa - return kappa (max level)
        return GreekLetters[9]; // kappa
    }

    /// <summary>
    /// Gets all available level labels in order.
    /// </summary>
    public static IReadOnlyList<string> GetAllLevelLabels()
    {
        var labels = new List<string>();
        for (int i = 1; i <= 20; i++)
        {
            labels.Add(GetLevelLabel(i));
        }
        return labels;
    }

    /// <summary>
    /// Minimum level index (1).
    /// </summary>
    public const int MinLevel = 1;

    /// <summary>
    /// Maximum level index (20 = kappa).
    /// </summary>
    public const int MaxLevel = 20;

    /// <summary>
    /// Level index where Greek letters start (11 = alpha).
    /// </summary>
    public const int GreekStartLevel = 11;

    /// <summary>
    /// Parses a level label back to its index.
    /// </summary>
    public static int ParseLevelLabel(string label)
    {
        if (string.IsNullOrEmpty(label))
            return 0;

        // Try numeric first
        if (int.TryParse(label, out var numericLevel))
        {
            return Math.Clamp(numericLevel, MinLevel, 10);
        }

        // Try Greek letter name (case-insensitive)
        for (int i = 0; i < GreekLetters.Length; i++)
        {
            if (string.Equals(label, GreekLetters[i], StringComparison.OrdinalIgnoreCase))
                return i + 11;
        }

        return 0;
    }

    public override string ToString()
    {
        return $"Level {Level} ({Confidence:P0} confidence)";
    }
}
