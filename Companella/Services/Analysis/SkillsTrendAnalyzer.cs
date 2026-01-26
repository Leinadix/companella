using Companella.Models.Session;
using Companella.Services.Database;

namespace Companella.Services.Analysis;

/// <summary>
/// Service for analyzing skill trends across multiple sessions.
/// Combines plays from multiple sessions into a unified timeline and calculates trends.
/// </summary>
public class SkillsTrendAnalyzer
{
	private readonly SessionDatabaseService _sessionDatabase;

	/// <summary>
	/// Minimum number of plays required for trend analysis.
	/// </summary>
	private const int _minPlaysForTrend = 5;

	/// <summary>
	/// Window size for moving average calculation.
	/// </summary>
	private const int _movingAverageWindow = 10;

	/// <summary>
	/// Minimum change in slope to detect a phase shift.
	/// </summary>
	private const double _phaseShiftThreshold = 0.5;

	/// <summary>
	/// Creates a new SkillsTrendAnalyzer.
	/// </summary>
	public SkillsTrendAnalyzer(SessionDatabaseService sessionDatabase)
	{
		_sessionDatabase = sessionDatabase;
	}

	/// <summary>
	/// Analyzes skill trends for a specific time period.
	/// </summary>
	/// <param name="timeRegion">The time region to analyze.</param>
	public SkillsTrendResult AnalyzeTrends(TimeRegion timeRegion)
	{
		var (start, end) = GetTimeRange(timeRegion);
		return AnalyzeTrends(start, end);
	}

	/// <summary>
	/// Analyzes skill trends for a specific date range.
	/// </summary>
	public SkillsTrendResult AnalyzeTrends(DateTime startTime, DateTime endTime)
	{
		// Get all plays in the time range
		var plays = _sessionDatabase.GetPlaysInTimeRange(startTime, endTime);

		// Convert to SkillsPlayData and order by time
		var orderedPlays = plays
			.Select(SkillsPlayData.FromStoredPlay)
			.OrderBy(p => p.PlayedAt)
			.ToList();

		// For All Time mode (DateTime.MinValue start), use actual play dates
		var actualStartTime = startTime == DateTime.MinValue && orderedPlays.Count > 0
			? orderedPlays.First().PlayedAt
			: startTime;

		var actualEndTime = startTime == DateTime.MinValue && orderedPlays.Count > 0
			? orderedPlays.Last().PlayedAt
			: endTime;

		var result = new SkillsTrendResult
		{
			StartTime = actualStartTime,
			EndTime = actualEndTime,
			Plays = orderedPlays
		};

		if (orderedPlays.Count == 0) return result;

		// Calculate current skill levels per skillset
		result.CurrentSkillLevels = CalculateCurrentSkillLevels(result.Plays);

		// Calculate trend slopes per skillset
		if (result.Plays.Count >= _minPlaysForTrend)
		{
			result.TrendSlopes = CalculateTrendSlopes(result.Plays);
			result.PhaseShifts = DetectPhaseShifts(result.Plays);
		}

		return result;
	}

	/// <summary>
	/// Gets the date range for a time region.
	/// </summary>
	private static (DateTime Start, DateTime End) GetTimeRange(TimeRegion region)
	{
		var end = DateTime.UtcNow;
		var start = region switch
		{
			TimeRegion.LastWeek => end.AddDays(-7),
			TimeRegion.LastMonth => end.AddMonths(-1),
			TimeRegion.Last3Months => end.AddMonths(-3),
			TimeRegion.AllTime => DateTime.MinValue, // Will be adjusted to first play date in AnalyzeTrends
			_ => DateTime.MinValue
		};
		return (start, end);
	}

	/// <summary>
	/// Calculates current skill levels per skillset using recent plays.
	/// Uses a weighted average favoring more recent plays.
	/// </summary>
	private static Dictionary<string, double> CalculateCurrentSkillLevels(List<SkillsPlayData> plays)
	{
		var skillLevels = new Dictionary<string, double>();
		var skillsetPlays = new Dictionary<string, List<(double Msd, double Weight, double Accuracy)>>();

		// Initialize skillsets
		var skillsets = new[]
			{ "stream", "jumpstream", "handstream", "stamina", "jackspeed", "chordjack", "technical" };
		foreach (var skillset in skillsets) skillsetPlays[skillset] = new List<(double, double, double)>();

		// Group plays by skillset with time-based weighting
		var totalPlays = plays.Count;
		for (var i = 0; i < plays.Count; i++)
		{
			var play = plays[i];
			var skillset = play.DominantSkillset.ToLowerInvariant();

			if (!skillsetPlays.ContainsKey(skillset))
				continue;

			// Weight more recent plays higher (exponential decay)
			var recencyWeight = Math.Pow(0.95, totalPlays - i - 1);

			// Also weight by accuracy (higher accuracy = more representative)
			// Uses 80-100% range: 80% = 0 weight, 100% = 1 weight
			var accuracyWeight = Math.Max(0, (play.Accuracy - 80) / 20.0);

			var combinedWeight = recencyWeight * accuracyWeight;

			skillsetPlays[skillset].Add((play.HighestMsdValue, combinedWeight, play.Accuracy));
		}

		// Calculate weighted average for each skillset
		foreach (var (skillset, playsData) in skillsetPlays)
		{
			if (playsData.Count == 0)
			{
				skillLevels[skillset] = 0;
				continue;
			}

			var totalWeight = playsData.Sum(p => p.Weight);
			if (totalWeight <= 0)
			{
				skillLevels[skillset] = playsData.Average(p => p.Msd);
			}
			else
			{
				var weightedSum = playsData.Sum(p => p.Msd * p.Weight);
				skillLevels[skillset] = weightedSum / totalWeight;
			}
		}

		// Calculate overall as weighted average of all skillsets
		var nonZeroSkills = skillLevels.Where(kvp => kvp.Value > 0).ToList();
		if (nonZeroSkills.Count > 0)
			skillLevels["overall"] = nonZeroSkills.Average(kvp => kvp.Value);
		else
			skillLevels["overall"] = 0;

		return skillLevels;
	}

	/// <summary>
	/// Calculates trend slopes per skillset using linear regression.
	/// Positive slope = improving, negative = declining.
	/// </summary>
	private static Dictionary<string, double> CalculateTrendSlopes(List<SkillsPlayData> plays)
	{
		var slopes = new Dictionary<string, double>();
		var skillsetPlays = GroupBySkillset(plays);

		foreach (var (skillset, skillPlays) in skillsetPlays)
		{
			if (skillPlays.Count < _minPlaysForTrend)
			{
				slopes[skillset] = 0;
				continue;
			}

			// Convert to (x, y) where x = normalized time, y = MSD
			var startTime = skillPlays.First().PlayedAt;
			var endTime = skillPlays.Last().PlayedAt;
			var timeRange = (endTime - startTime).TotalHours;

			if (timeRange <= 0)
			{
				slopes[skillset] = 0;
				continue;
			}

			var points = skillPlays
				.Select(p => (
					X: (p.PlayedAt - startTime).TotalHours / timeRange,
					Y: (double)p.HighestMsdValue
				))
				.ToList();

			slopes[skillset] = CalculateLinearRegressionSlope(points);
		}

		// Calculate overall slope
		if (plays.Count >= _minPlaysForTrend)
		{
			var startTime = plays.First().PlayedAt;
			var endTime = plays.Last().PlayedAt;
			var timeRange = (endTime - startTime).TotalHours;

			if (timeRange > 0)
			{
				var points = plays
					.Select(p => (
						X: (p.PlayedAt - startTime).TotalHours / timeRange,
						Y: (double)p.HighestMsdValue
					))
					.ToList();
				slopes["overall"] = CalculateLinearRegressionSlope(points);
			}
			else
			{
				slopes["overall"] = 0;
			}
		}
		else
		{
			slopes["overall"] = 0;
		}

		return slopes;
	}

	/// <summary>
	/// Groups plays by their dominant skillset.
	/// </summary>
	private static Dictionary<string, List<SkillsPlayData>> GroupBySkillset(List<SkillsPlayData> plays)
	{
		var groups = new Dictionary<string, List<SkillsPlayData>>(StringComparer.OrdinalIgnoreCase);
		var skillsets = new[]
			{ "stream", "jumpstream", "handstream", "stamina", "jackspeed", "chordjack", "technical" };

		foreach (var skillset in skillsets) groups[skillset] = new List<SkillsPlayData>();

		foreach (var play in plays)
		{
			var skillset = play.DominantSkillset.ToLowerInvariant();
			if (groups.TryGetValue(skillset, out var list)) list.Add(play);
		}

		return groups;
	}

	/// <summary>
	/// Calculates the slope of a linear regression line through the points.
	/// </summary>
	private static double CalculateLinearRegressionSlope(List<(double X, double Y)> points)
	{
		if (points.Count < 2)
			return 0;

		var n = points.Count;
		var sumX = points.Sum(p => p.X);
		var sumY = points.Sum(p => p.Y);
		var sumXY = points.Sum(p => p.X * p.Y);
		var sumX2 = points.Sum(p => p.X * p.X);

		var denominator = n * sumX2 - sumX * sumX;
		if (Math.Abs(denominator) < 0.0001)
			return 0;

		return (n * sumXY - sumX * sumY) / denominator;
	}

	/// <summary>
	/// Detects phase shifts (significant changes in skill progression).
	/// </summary>
	private static List<PhaseShiftPoint> DetectPhaseShifts(List<SkillsPlayData> plays)
	{
		var phaseShifts = new List<PhaseShiftPoint>();

		if (plays.Count < _movingAverageWindow * 2)
			return phaseShifts;

		// Calculate moving average of MSD values
		var movingAverages = new List<double>();
		for (var i = 0; i <= plays.Count - _movingAverageWindow; i++)
		{
			var window = plays.Skip(i).Take(_movingAverageWindow);
			movingAverages.Add(window.Average(p => p.HighestMsdValue));
		}

		// Detect significant changes in the moving average
		for (var i = 1; i < movingAverages.Count - 1; i++)
		{
			var prevSlope = movingAverages[i] - movingAverages[i - 1];
			var nextSlope = movingAverages[i + 1] - movingAverages[i];
			var slopeChange = nextSlope - prevSlope;

			if (Math.Abs(slopeChange) > _phaseShiftThreshold)
			{
				var correspondingPlay = plays[i + _movingAverageWindow / 2];
				var type = DeterminePhaseShiftType(prevSlope, nextSlope);

				phaseShifts.Add(new PhaseShiftPoint
				{
					Time = correspondingPlay.PlayedAt,
					Type = type,
					Magnitude = slopeChange,
					AffectedSkillsets = new List<string> { correspondingPlay.DominantSkillset },
					Description = GeneratePhaseShiftDescription(type, slopeChange)
				});
			}
		}

		// Merge nearby phase shifts
		return MergeNearbyPhaseShifts(phaseShifts);
	}

	/// <summary>
	/// Determines the type of phase shift based on slope changes.
	/// </summary>
	private static PhaseShiftType DeterminePhaseShiftType(double prevSlope, double nextSlope)
	{
		if (prevSlope <= 0 && nextSlope > 0.5)
			return PhaseShiftType.Breakthrough;
		if (prevSlope >= 0 && nextSlope < -0.5)
			return PhaseShiftType.Decline;
		if (Math.Abs(prevSlope) > 0.5 && Math.Abs(nextSlope) < 0.2)
			return PhaseShiftType.Plateau;
		if (prevSlope < -0.3 && nextSlope > 0)
			return PhaseShiftType.Recovery;

		return nextSlope > prevSlope ? PhaseShiftType.Breakthrough : PhaseShiftType.Decline;
	}

	/// <summary>
	/// Generates a human-readable description for a phase shift.
	/// </summary>
	private static string GeneratePhaseShiftDescription(PhaseShiftType type, double magnitude)
	{
		return type switch
		{
			PhaseShiftType.Breakthrough => $"Started rapid improvement (magnitude: {magnitude:+0.00})",
			PhaseShiftType.Plateau => "Entered a plateau period",
			PhaseShiftType.Decline => $"Performance started declining (magnitude: {magnitude:0.00})",
			PhaseShiftType.Recovery => "Started recovering from decline",
			_ => "Skill progression changed"
		};
	}

	/// <summary>
	/// Merges phase shifts that are close together in time.
	/// </summary>
	private static List<PhaseShiftPoint> MergeNearbyPhaseShifts(List<PhaseShiftPoint> shifts)
	{
		if (shifts.Count < 2)
			return shifts;

		var merged = new List<PhaseShiftPoint>();
		var mergeWindow = TimeSpan.FromHours(2);

		var current = shifts[0];
		for (var i = 1; i < shifts.Count; i++)
			if (shifts[i].Time - current.Time < mergeWindow && shifts[i].Type == current.Type)
			{
				// Merge: take the larger magnitude
				if (Math.Abs(shifts[i].Magnitude) > Math.Abs(current.Magnitude)) current = shifts[i];

				current.AffectedSkillsets = current.AffectedSkillsets
					.Union(shifts[i].AffectedSkillsets)
					.Distinct()
					.ToList();
			}
			else
			{
				merged.Add(current);
				current = shifts[i];
			}

		merged.Add(current);

		return merged;
	}
}

/// <summary>
/// Predefined time regions for trend analysis.
/// </summary>
public enum TimeRegion
{
	/// <summary>
	/// Last 7 days.
	/// </summary>
	LastWeek,

	/// <summary>
	/// Last 30 days.
	/// </summary>
	LastMonth,

	/// <summary>
	/// Last 90 days.
	/// </summary>
	Last3Months,

	/// <summary>
	/// All recorded history.
	/// </summary>
	AllTime
}
