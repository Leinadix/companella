namespace Companella.Models.Session;

/// <summary>
/// Represents the result of skills trend analysis across multiple sessions.
/// </summary>
public class SkillsTrendResult
{
    /// <summary>
    /// Combined play data from all analyzed sessions, ordered by time.
    /// </summary>
    public List<SkillsPlayData> Plays { get; set; } = new();

    /// <summary>
    /// Trend slopes per skillset (positive = improving, negative = declining).
    /// </summary>
    public Dictionary<string, double> TrendSlopes { get; set; } = new();

    /// <summary>
    /// Current estimated skill level per skillset.
    /// </summary>
    public Dictionary<string, double> CurrentSkillLevels { get; set; } = new();

    /// <summary>
    /// Phase shift points where significant changes in progression occurred.
    /// </summary>
    public List<PhaseShiftPoint> PhaseShifts { get; set; } = new();

    /// <summary>
    /// The time range analyzed.
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// The end of the time range analyzed.
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// Total number of plays analyzed.
    /// </summary>
    public int TotalPlays => Plays.Count;

    /// <summary>
    /// Average accuracy across all plays.
    /// </summary>
    public double AverageAccuracy => Plays.Count > 0 ? Plays.Average(p => p.Accuracy) : 0;

    /// <summary>
    /// Gets the weakest skillsets based on current skill levels.
    /// </summary>
    /// <param name="count">Number of weakest skillsets to return.</param>
    public List<string> GetWeakestSkillsets(int count = 3)
    {
        return CurrentSkillLevels
            .Where(kvp => kvp.Key != "overall" && kvp.Key != "unknown")
            .OrderBy(kvp => kvp.Value)
            .Take(count)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <summary>
    /// Gets the strongest skillsets based on current skill levels.
    /// </summary>
    /// <param name="count">Number of strongest skillsets to return.</param>
    public List<string> GetStrongestSkillsets(int count = 3)
    {
        return CurrentSkillLevels
            .Where(kvp => kvp.Key != "overall" && kvp.Key != "unknown")
            .OrderByDescending(kvp => kvp.Value)
            .Take(count)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <summary>
    /// Gets the overall skill level (average of all skillsets).
    /// </summary>
    public double OverallSkillLevel
    {
        get
        {
            var relevantSkills = CurrentSkillLevels
                .Where(kvp => kvp.Key != "overall" && kvp.Key != "unknown")
                .Select(kvp => kvp.Value)
                .ToList();
            return relevantSkills.Count > 0 ? relevantSkills.Average() : 0;
        }
    }
}

/// <summary>
/// Represents a single play's data for trend analysis.
/// </summary>
public class SkillsPlayData
{
    /// <summary>
    /// The absolute time when this play occurred.
    /// </summary>
    public DateTime PlayedAt { get; set; }

    /// <summary>
    /// Path to the beatmap file.
    /// </summary>
    public string BeatmapPath { get; set; } = string.Empty;

    /// <summary>
    /// The accuracy achieved.
    /// </summary>
    public double Accuracy { get; set; }

    /// <summary>
    /// The MSD value of the highest skillset.
    /// </summary>
    public float HighestMsdValue { get; set; }

    /// <summary>
    /// The dominant skillset of the map.
    /// </summary>
    public string DominantSkillset { get; set; } = string.Empty;

    /// <summary>
    /// The session ID this play belongs to.
    /// </summary>
    public long SessionId { get; set; }

    /// <summary>
    /// Creates a SkillsPlayData from a StoredSessionPlay.
    /// </summary>
    public static SkillsPlayData FromStoredPlay(StoredSessionPlay play)
    {
        return new SkillsPlayData
        {
            PlayedAt = play.RecordedAt,
            BeatmapPath = play.BeatmapPath,
            Accuracy = play.Accuracy,
            HighestMsdValue = play.HighestMsdValue,
            DominantSkillset = play.DominantSkillset,
            SessionId = play.SessionId
        };
    }
}

/// <summary>
/// Represents a point where a significant change in skill progression occurred.
/// </summary>
public class PhaseShiftPoint
{
    /// <summary>
    /// The time when the phase shift occurred.
    /// </summary>
    public DateTime Time { get; set; }

    /// <summary>
    /// The type of phase shift.
    /// </summary>
    public PhaseShiftType Type { get; set; }

    /// <summary>
    /// The skillsets affected by this phase shift.
    /// </summary>
    public List<string> AffectedSkillsets { get; set; } = new();

    /// <summary>
    /// The magnitude of the change (positive = improvement, negative = decline).
    /// </summary>
    public double Magnitude { get; set; }

    /// <summary>
    /// A human-readable description of the phase shift.
    /// </summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Types of phase shifts in skill progression.
/// </summary>
public enum PhaseShiftType
{
    /// <summary>
    /// A period of rapid improvement.
    /// </summary>
    Breakthrough,

    /// <summary>
    /// A period of stagnation or slow progress.
    /// </summary>
    Plateau,

    /// <summary>
    /// A period of declining performance.
    /// </summary>
    Decline,

    /// <summary>
    /// Recovery from a decline period.
    /// </summary>
    Recovery
}

