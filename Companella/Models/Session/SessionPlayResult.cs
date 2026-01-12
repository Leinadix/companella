namespace Companella.Models.Session;

/// <summary>
/// Represents a single play result recorded during a session tracking session.
/// </summary>
public class SessionPlayResult
{
    /// <summary>
    /// Full path to the .osu beatmap file that was played.
    /// </summary>
    public string BeatmapPath { get; set; } = string.Empty;

    /// <summary>
    /// The accuracy achieved on this play (0.0 to 100.0).
    /// </summary>
    public double Accuracy { get; set; }

    /// <summary>
    /// The timestamp when this play was recorded (relative to session start).
    /// </summary>
    public TimeSpan SessionTime { get; set; }

    /// <summary>
    /// The UTC time when this play was recorded.
    /// </summary>
    public DateTime RecordedAt { get; set; }

    /// <summary>
    /// The highest MSD stat value (excluding overall) for this map.
    /// </summary>
    public float HighestMsdValue { get; set; }

    /// <summary>
    /// The name of the skillset with the highest MSD value.
    /// </summary>
    public string DominantSkillset { get; set; } = string.Empty;

    /// <summary>
    /// Creates a new empty SessionPlayResult.
    /// </summary>
    public SessionPlayResult()
    {
    }

    /// <summary>
    /// Creates a new SessionPlayResult with all properties set.
    /// </summary>
    public SessionPlayResult(
        string beatmapPath,
        double accuracy,
        TimeSpan sessionTime,
        DateTime recordedAt,
        float highestMsdValue,
        string dominantSkillset)
    {
        BeatmapPath = beatmapPath;
        Accuracy = accuracy;
        SessionTime = sessionTime;
        RecordedAt = recordedAt;
        HighestMsdValue = highestMsdValue;
        DominantSkillset = dominantSkillset;
    }

    /// <summary>
    /// Gets the beatmap filename without the full path.
    /// </summary>
    public string BeatmapFileName => Path.GetFileName(BeatmapPath);

    /// <summary>
    /// Gets the accuracy as a normalized value (0.0 to 1.0).
    /// </summary>
    public double AccuracyNormalized => Accuracy / 100.0;

    /// <summary>
    /// Gets the session time formatted as hh:mm:ss.
    /// </summary>
    public string SessionTimeFormatted => SessionTime.ToString(@"hh\:mm\:ss");
}

