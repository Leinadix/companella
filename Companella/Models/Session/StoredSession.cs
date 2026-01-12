namespace Companella.Models.Session;

/// <summary>
/// Represents a session stored in the database.
/// </summary>
public class StoredSession
{
    /// <summary>
    /// Unique identifier for the session.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// When the session started (UTC).
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// When the session ended (UTC).
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// Total number of plays in this session.
    /// </summary>
    public int TotalPlays { get; set; }

    /// <summary>
    /// Average accuracy across all plays.
    /// </summary>
    public double AverageAccuracy { get; set; }

    /// <summary>
    /// Best accuracy achieved in this session.
    /// </summary>
    public double BestAccuracy { get; set; }

    /// <summary>
    /// Worst accuracy achieved in this session.
    /// </summary>
    public double WorstAccuracy { get; set; }

    /// <summary>
    /// Average MSD rating of maps played.
    /// </summary>
    public double AverageMsd { get; set; }

    /// <summary>
    /// Total time played in this session.
    /// </summary>
    public TimeSpan TotalTimePlayed { get; set; }

    /// <summary>
    /// List of individual plays in this session (loaded separately).
    /// </summary>
    public List<StoredSessionPlay> Plays { get; set; } = new();

    /// <summary>
    /// Gets the session duration.
    /// </summary>
    public TimeSpan Duration => EndTime - StartTime;

    /// <summary>
    /// Gets a display string for the session (for dropdown).
    /// </summary>
    public string DisplayName => $"{StartTime.ToLocalTime():MMM dd, yyyy HH:mm} - {EndTime.ToLocalTime():HH:mm} ({TotalPlays} plays)";

    /// <summary>
    /// Gets a short display string.
    /// </summary>
    public string ShortDisplayName => $"{StartTime.ToLocalTime():MMM dd HH:mm} ({TotalPlays} plays)";

    /// <summary>
    /// Gets skillset distribution as a dictionary.
    /// </summary>
    public Dictionary<string, int> GetSkillsetDistribution()
    {
        var distribution = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var play in Plays)
        {
            var skillset = play.DominantSkillset;
            if (string.IsNullOrEmpty(skillset))
                skillset = "unknown";
            
            if (distribution.ContainsKey(skillset))
                distribution[skillset]++;
            else
                distribution[skillset] = 1;
        }
        
        return distribution;
    }

    /// <summary>
    /// Gets the best play in the session.
    /// </summary>
    public StoredSessionPlay? GetBestPlay()
    {
        return Plays.MaxBy(p => p.Accuracy);
    }

    /// <summary>
    /// Gets the worst play in the session.
    /// </summary>
    public StoredSessionPlay? GetWorstPlay()
    {
        return Plays.MinBy(p => p.Accuracy);
    }

    /// <summary>
    /// Gets the highest MSD play in the session.
    /// </summary>
    public StoredSessionPlay? GetHighestMsdPlay()
    {
        return Plays.MaxBy(p => p.HighestMsdValue);
    }
}

/// <summary>
/// Represents a single play stored in the database.
/// </summary>
public class StoredSessionPlay
{
    /// <summary>
    /// Unique identifier for the play.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// The session this play belongs to.
    /// </summary>
    public long SessionId { get; set; }

    /// <summary>
    /// Full path to the .osu beatmap file.
    /// </summary>
    public string BeatmapPath { get; set; } = string.Empty;

    /// <summary>
    /// The accuracy achieved (0.0 to 100.0).
    /// </summary>
    public double Accuracy { get; set; }

    /// <summary>
    /// Time since session start when this play was recorded.
    /// </summary>
    public TimeSpan SessionTime { get; set; }

    /// <summary>
    /// UTC time when this play was recorded.
    /// </summary>
    public DateTime RecordedAt { get; set; }

    /// <summary>
    /// The highest MSD stat value (excluding overall).
    /// </summary>
    public float HighestMsdValue { get; set; }

    /// <summary>
    /// The name of the dominant skillset.
    /// </summary>
    public string DominantSkillset { get; set; } = string.Empty;

    /// <summary>
    /// Gets the beatmap filename without path.
    /// </summary>
    public string BeatmapFileName => Path.GetFileName(BeatmapPath);

    /// <summary>
    /// Gets the session time formatted as hh:mm:ss.
    /// </summary>
    public string SessionTimeFormatted => SessionTime.ToString(@"hh\:mm\:ss");

    /// <summary>
    /// Creates a StoredSessionPlay from a SessionPlayResult.
    /// </summary>
    public static StoredSessionPlay FromPlayResult(SessionPlayResult result, long sessionId)
    {
        return new StoredSessionPlay
        {
            SessionId = sessionId,
            BeatmapPath = result.BeatmapPath,
            Accuracy = result.Accuracy,
            SessionTime = result.SessionTime,
            RecordedAt = result.RecordedAt,
            HighestMsdValue = result.HighestMsdValue,
            DominantSkillset = result.DominantSkillset
        };
    }
}

