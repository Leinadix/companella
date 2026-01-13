namespace Companella.Models.Session;

/// <summary>
/// Mode for generating a session plan.
/// </summary>
public enum SessionPlanMode
{
    /// <summary>
    /// Use analysis data from recent sessions to determine difficulty levels.
    /// </summary>
    Analysis,

    /// <summary>
    /// Manually specify focus skillset and target difficulty.
    /// </summary>
    Manual
}

/// <summary>
/// Phase of a practice session.
/// </summary>
public enum SessionPhase
{
    /// <summary>
    /// Warmup phase with consistency maps (~15 min).
    /// </summary>
    Warmup,

    /// <summary>
    /// Ramp-up phase with increasing difficulty (~45 min).
    /// </summary>
    RampUp,

    /// <summary>
    /// Cooldown phase with decreasing difficulty (~20 min).
    /// </summary>
    Cooldown
}

/// <summary>
/// Represents a single map in a session plan.
/// </summary>
public class SessionPlanItem
{
    /// <summary>
    /// The index of this item in the session (1-based for display).
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Path to the original .osu file.
    /// </summary>
    public string OriginalPath { get; set; } = string.Empty;

    /// <summary>
    /// Path to the indexed copy of the .osu file (with #INDEX prefix in version).
    /// </summary>
    public string IndexedPath { get; set; } = string.Empty;

    /// <summary>
    /// The phase this item belongs to.
    /// </summary>
    public SessionPhase Phase { get; set; }

    /// <summary>
    /// The target MSD for this slot in the session.
    /// </summary>
    public double TargetMsd { get; set; }

    /// <summary>
    /// The actual MSD of the selected map.
    /// </summary>
    public double ActualMsd { get; set; }

    /// <summary>
    /// Display name of the map.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Estimated duration of the map in seconds.
    /// </summary>
    public int EstimatedDurationSeconds { get; set; }

    /// <summary>
    /// The dominant skillset of the map.
    /// </summary>
    public string Skillset { get; set; } = string.Empty;
}

/// <summary>
/// Represents a complete session plan with all phases.
/// </summary>
public class SessionPlan
{
    /// <summary>
    /// The mode used to generate this plan.
    /// </summary>
    public SessionPlanMode Mode { get; set; }

    /// <summary>
    /// The focus skillset (for manual mode or analysis-derived).
    /// </summary>
    public string? FocusSkillset { get; set; }

    /// <summary>
    /// All items in the session, ordered by index.
    /// </summary>
    public List<SessionPlanItem> Items { get; set; } = new();

    /// <summary>
    /// The collection name for this session.
    /// </summary>
    public string CollectionName { get; set; } = string.Empty;

    /// <summary>
    /// When the plan was generated.
    /// </summary>
    public DateTime GeneratedAt { get; set; }

    /// <summary>
    /// The warmup difficulty level (MSD).
    /// </summary>
    public double WarmupDifficulty { get; set; }

    /// <summary>
    /// The peak difficulty level (MSD).
    /// </summary>
    public double PeakDifficulty { get; set; }

    /// <summary>
    /// The cooldown end difficulty level (MSD).
    /// </summary>
    public double CooldownEndDifficulty { get; set; }

    /// <summary>
    /// The MSD curve configuration used to generate this plan (if curve-based).
    /// </summary>
    public MsdCurveConfig? CurveConfig { get; set; }

    /// <summary>
    /// Gets items for a specific phase.
    /// </summary>
    public List<SessionPlanItem> GetPhaseItems(SessionPhase phase)
    {
        return Items.Where(i => i.Phase == phase).ToList();
    }

    /// <summary>
    /// Gets total estimated duration in minutes.
    /// </summary>
    public double TotalDurationMinutes => Items.Sum(i => i.EstimatedDurationSeconds) / 60.0;

    /// <summary>
    /// Gets estimated duration for a phase in minutes.
    /// </summary>
    public double GetPhaseDurationMinutes(SessionPhase phase)
    {
        return GetPhaseItems(phase).Sum(i => i.EstimatedDurationSeconds) / 60.0;
    }

    /// <summary>
    /// Gets the number of maps in the session.
    /// </summary>
    public int MapCount => Items.Count;

    /// <summary>
    /// Summary of the session plan.
    /// </summary>
    public string Summary => $"{MapCount} maps, ~{TotalDurationMinutes:F0} min total " +
        $"(Warmup: {GetPhaseDurationMinutes(SessionPhase.Warmup):F0}m, " +
        $"Ramp: {GetPhaseDurationMinutes(SessionPhase.RampUp):F0}m, " +
        $"Cool: {GetPhaseDurationMinutes(SessionPhase.Cooldown):F0}m)";
}

/// <summary>
/// Configuration for session plan generation.
/// </summary>
public class SessionPlanConfig
{
    /// <summary>
    /// Target warmup duration in minutes.
    /// </summary>
    public int WarmupMinutes { get; set; } = 15;

    /// <summary>
    /// Target ramp-up duration in minutes.
    /// </summary>
    public int RampUpMinutes { get; set; } = 45;

    /// <summary>
    /// Target cooldown duration in minutes.
    /// </summary>
    public int CooldownMinutes { get; set; } = 20;

    /// <summary>
    /// Percentage below consistency level for warmup (0.1 = 10%).
    /// </summary>
    public double WarmupDifficultyReduction { get; set; } = 0.10;

    /// <summary>
    /// Percentage below peak for cooldown end (0.15 = 15%).
    /// </summary>
    public double CooldownDifficultyReduction { get; set; } = 0.15;

    /// <summary>
    /// MSD tolerance when searching for maps.
    /// </summary>
    public double MsdTolerance { get; set; } = 0.5;

    /// <summary>
    /// Default map duration estimate in seconds (when actual duration unknown).
    /// </summary>
    public int DefaultMapDurationSeconds { get; set; } = 120;
}

