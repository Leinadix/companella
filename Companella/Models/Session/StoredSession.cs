using System.Globalization;

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
	public string DisplayName =>
		$"{StartTime.ToLocalTime():MMM dd, yyyy HH:mm} - {EndTime.ToLocalTime():HH:mm} ({TotalPlays} plays)";

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

			distribution[skillset] = distribution.GetValueOrDefault(skillset) + 1;
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
	/// MD5 hash of the beatmap file.
	/// </summary>
	public string BeatmapHash { get; set; } = string.Empty;

	/// <summary>
	/// The accuracy achieved (0.0 to 100.0).
	/// </summary>
	public double Accuracy { get; set; }

	/// <summary>
	/// Number of misses during this play.
	/// </summary>
	public int Misses { get; set; }

	/// <summary>
	/// Number of times the player paused during this play.
	/// </summary>
	public int PauseCount { get; set; }

	/// <summary>
	/// The grade achieved (SS, S, A, B, C, D, F, Q).
	/// </summary>
	public string Grade { get; set; } = "D";

	/// <summary>
	/// Status of the play (Completed, Failed, Quit).
	/// </summary>
	public PlayStatus Status { get; set; } = PlayStatus.Completed;

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
	/// MD5 hash of the replay file (null until replay is found).
	/// </summary>
	public string? ReplayHash { get; set; }

	/// <summary>
	/// Full path to the replay file (null until replay is found).
	/// </summary>
	public string? ReplayPath { get; set; }

	/// <summary>
	/// The rate multiplier used during this play (1.5 for DT/NC, 0.75 for HT, 1.0 for normal).
	/// </summary>
	public float Rate { get; set; } = 1.0f;

	/// <summary>
	/// Gets the beatmap filename without path.
	/// </summary>
	public string BeatmapFileName => Path.GetFileName(BeatmapPath);

	/// <summary>
	/// Gets the session time formatted as hh:mm:ss.
	/// </summary>
	public string SessionTimeFormatted => SessionTime.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

	/// <summary>
	/// Whether this play has a replay file available.
	/// </summary>
	public bool HasReplay => !string.IsNullOrEmpty(ReplayPath) && File.Exists(ReplayPath);

	/// <summary>
	/// Creates a StoredSessionPlay from a SessionPlayResult.
	/// </summary>
	public static StoredSessionPlay FromPlayResult(SessionPlayResult result, long sessionId)
	{
		return new StoredSessionPlay
		{
			SessionId = sessionId,
			BeatmapPath = result.BeatmapPath,
			BeatmapHash = result.BeatmapHash,
			Accuracy = result.Accuracy,
			Misses = result.Misses,
			PauseCount = result.PauseCount,
			Grade = result.Grade,
			Status = result.Status,
			SessionTime = result.SessionTime,
			RecordedAt = result.RecordedAt,
			HighestMsdValue = result.HighestMsdValue,
			DominantSkillset = result.DominantSkillset,
			ReplayHash = result.ReplayHash,
			ReplayPath = result.ReplayPath,
			Rate = result.Rate
		};
	}
}
