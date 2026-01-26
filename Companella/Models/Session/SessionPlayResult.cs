using System.Globalization;
namespace Companella.Models.Session;

/// <summary>
/// Play status indicating how the play ended.
/// </summary>
public enum PlayStatus
{
	/// <summary>
	/// Play was completed successfully (reached results screen).
	/// </summary>
	Completed,

	/// <summary>
	/// Play was failed (HP dropped to 0).
	/// </summary>
	Failed,

	/// <summary>
	/// Play was quit by the player.
	/// </summary>
	Quit
}

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
	/// MD5 hash of the beatmap file.
	/// </summary>
	public string BeatmapHash { get; set; } = string.Empty;

	/// <summary>
	/// The accuracy achieved on this play (0.0 to 100.0).
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
		string beatmapHash,
		double accuracy,
		int misses,
		int pauseCount,
		PlayStatus status,
		TimeSpan sessionTime,
		DateTime recordedAt,
		float highestMsdValue,
		string dominantSkillset,
		float rate = 1.0f)
	{
		BeatmapPath = beatmapPath;
		BeatmapHash = beatmapHash;
		Accuracy = accuracy;
		Misses = misses;
		PauseCount = pauseCount;
		Status = status;
		SessionTime = sessionTime;
		RecordedAt = recordedAt;
		HighestMsdValue = highestMsdValue;
		DominantSkillset = dominantSkillset;
		Rate = rate;
		Grade = CalculateGrade(accuracy, misses, status);
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
	public string SessionTimeFormatted => SessionTime.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

	/// <summary>
	/// Whether this play has a replay file available.
	/// </summary>
	public bool HasReplay => !string.IsNullOrEmpty(ReplayPath) && File.Exists(ReplayPath);

	/// <summary>
	/// Calculates the grade based on accuracy, misses, and play status.
	/// </summary>
	/// <param name="accuracy">Accuracy achieved (0-100).</param>
	/// <param name="misses">Number of misses.</param>
	/// <param name="status">Play status.</param>
	/// <returns>Grade string (SS, S, A, B, C, D, F, Q).</returns>
	public static string CalculateGrade(double accuracy, int misses, PlayStatus status)
	{
		// Quit always gets Q grade
		if (status == PlayStatus.Quit)
			return "Q";

		// Failed always gets F grade
		if (status == PlayStatus.Failed)
			return "F";

		// SS requires 100% accuracy and 0 misses
		if (accuracy >= 100.0 && misses == 0)
			return "SS";

		// Grade thresholds
		if (accuracy >= 95.0)
			return "S";
		if (accuracy >= 90.0)
			return "A";
		if (accuracy >= 80.0)
			return "B";
		if (accuracy >= 70.0)
			return "C";
		if (accuracy >= 60.0)
			return "D";

		return "F";
	}

	/// <summary>
	/// Updates the grade based on current accuracy, misses, and status.
	/// </summary>
	public void UpdateGrade()
	{
		Grade = CalculateGrade(Accuracy, Misses, Status);
	}
}
