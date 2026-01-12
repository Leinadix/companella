namespace Companella.Models.Database;

/// <summary>
/// Represents a beatmap indexed in the maps database with MSD scores and metadata.
/// </summary>
public class IndexedMap
{
    /// <summary>
    /// Full path to the .osu file (primary key).
    /// </summary>
    public string BeatmapPath { get; set; } = string.Empty;

    /// <summary>
    /// MD5 hash of the file for change detection.
    /// </summary>
    public string FileHash { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes for quick change detection.
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Last modification time in ticks for quick change detection.
    /// </summary>
    public long LastModifiedTicks { get; set; }

    /// <summary>
    /// Song title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Song artist.
    /// </summary>
    public string Artist { get; set; } = string.Empty;

    /// <summary>
    /// Difficulty name.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Map creator.
    /// </summary>
    public string Creator { get; set; } = string.Empty;

    /// <summary>
    /// Circle size (key count for mania).
    /// </summary>
    public double CircleSize { get; set; }

    /// <summary>
    /// Game mode (3 = mania).
    /// </summary>
    public int Mode { get; set; }

    /// <summary>
    /// When this map was last analyzed.
    /// </summary>
    public DateTime LastAnalyzed { get; set; }

    /// <summary>
    /// MSD scores at various rates.
    /// </summary>
    public List<MapMsdScore> MsdScores { get; set; } = new();

    /// <summary>
    /// Player statistics for this map.
    /// </summary>
    public List<MapPlayerStat> PlayerStats { get; set; } = new();

    /// <summary>
    /// The dominant skillset at 1.0x rate.
    /// </summary>
    public string DominantSkillset { get; set; } = string.Empty;

    /// <summary>
    /// Overall MSD at 1.0x rate.
    /// </summary>
    public float OverallMsd { get; set; }

    /// <summary>
    /// Gets a display name for the map.
    /// </summary>
    public string DisplayName => $"{Artist} - {Title} [{Version}]";

    /// <summary>
    /// Gets the filename without path.
    /// </summary>
    public string FileName => Path.GetFileName(BeatmapPath);

    /// <summary>
    /// Gets MSD scores for a specific rate.
    /// </summary>
    public MapMsdScore? GetScoresForRate(float rate)
    {
        return MsdScores.FirstOrDefault(s => Math.Abs(s.Rate - rate) < 0.05f);
    }

    /// <summary>
    /// Gets the average player accuracy on this map.
    /// </summary>
    public double? AveragePlayerAccuracy
    {
        get
        {
            if (PlayerStats.Count == 0) return null;
            return PlayerStats.Average(s => s.Accuracy);
        }
    }

    /// <summary>
    /// Gets the best player accuracy on this map.
    /// </summary>
    public double? BestPlayerAccuracy
    {
        get
        {
            if (PlayerStats.Count == 0) return null;
            return PlayerStats.Max(s => s.Accuracy);
        }
    }

    /// <summary>
    /// Number of times this map has been played.
    /// </summary>
    public int PlayCount => PlayerStats.Count;
}

/// <summary>
/// MSD scores for a map at a specific rate.
/// </summary>
public class MapMsdScore
{
    /// <summary>
    /// The rate (0.7 to 2.0).
    /// </summary>
    public float Rate { get; set; }

    /// <summary>
    /// Overall MSD at this rate.
    /// </summary>
    public float Overall { get; set; }

    /// <summary>
    /// Stream MSD at this rate.
    /// </summary>
    public float Stream { get; set; }

    /// <summary>
    /// Jumpstream MSD at this rate.
    /// </summary>
    public float Jumpstream { get; set; }

    /// <summary>
    /// Handstream MSD at this rate.
    /// </summary>
    public float Handstream { get; set; }

    /// <summary>
    /// Stamina MSD at this rate.
    /// </summary>
    public float Stamina { get; set; }

    /// <summary>
    /// Jackspeed MSD at this rate.
    /// </summary>
    public float Jackspeed { get; set; }

    /// <summary>
    /// Chordjack MSD at this rate.
    /// </summary>
    public float Chordjack { get; set; }

    /// <summary>
    /// Technical MSD at this rate.
    /// </summary>
    public float Technical { get; set; }

    /// <summary>
    /// Gets the dominant skillset at this rate.
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

        return skillsets.MaxBy(s => s.Value);
    }

    /// <summary>
    /// Gets the MSD value for a specific skillset.
    /// </summary>
    public float GetSkillsetValue(string skillset)
    {
        return skillset.ToLowerInvariant() switch
        {
            "overall" => Overall,
            "stream" => Stream,
            "jumpstream" => Jumpstream,
            "handstream" => Handstream,
            "stamina" => Stamina,
            "jackspeed" => Jackspeed,
            "chordjack" => Chordjack,
            "technical" => Technical,
            _ => 0
        };
    }

    /// <summary>
    /// Gets all skillset values as a dictionary.
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
}

/// <summary>
/// Player statistics for a specific map.
/// </summary>
public class MapPlayerStat
{
    /// <summary>
    /// ID from the sessions database.
    /// </summary>
    public long SessionPlayId { get; set; }

    /// <summary>
    /// The accuracy achieved.
    /// </summary>
    public double Accuracy { get; set; }

    /// <summary>
    /// When this play was recorded.
    /// </summary>
    public DateTime RecordedAt { get; set; }
}
