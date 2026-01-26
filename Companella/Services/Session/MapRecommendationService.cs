using Companella.Models.Session;
using Companella.Models.Training;
using Companella.Services.Analysis;
using Companella.Services.Database;

namespace Companella.Services.Session;

/// <summary>
/// Service for generating map recommendations based on player skill and focus mode.
/// </summary>
public class MapRecommendationService
{
	private readonly MapsDatabaseService _mapsDatabase;
	private readonly MapMmrCalculator _mmrCalculator;
	private readonly SkillsTrendAnalyzer _trendAnalyzer;

	/// <summary>
	/// Default number of recommendations to generate.
	/// </summary>
	private const int _defaultRecommendationCount = 10;

	/// <summary>
	/// Creates a new MapRecommendationService.
	/// </summary>
	public MapRecommendationService(
		MapsDatabaseService mapsDatabase,
		MapMmrCalculator mmrCalculator,
		SkillsTrendAnalyzer trendAnalyzer)
	{
		_mapsDatabase = mapsDatabase;
		_mmrCalculator = mmrCalculator;
		_trendAnalyzer = trendAnalyzer;
	}

	/// <summary>
	/// Gets recommendations based on focus mode.
	/// </summary>
	/// <param name="focus">The recommendation focus mode.</param>
	/// <param name="trends">Player skill trends.</param>
	/// <param name="count">Number of recommendations to generate.</param>
	/// <param name="targetSkillset">Optional specific skillset (for Skillset focus).</param>
	public RecommendationBatch GetRecommendations(
		RecommendationFocus focus,
		SkillsTrendResult trends,
		int count = _defaultRecommendationCount,
		string? targetSkillset = null)
	{
		var batch = new RecommendationBatch
		{
			Focus = focus,
			TargetSkillset = targetSkillset,
			PlayerSkillLevel = trends.OverallSkillLevel
		};

		var recommendations = focus switch
		{
			RecommendationFocus.Skillset => GetSkillsetRecommendations(trends, targetSkillset, count),
			RecommendationFocus.Consistency => GetConsistencyRecommendations(trends, count),
			RecommendationFocus.Push => GetPushRecommendations(trends, count),
			RecommendationFocus.DeficitFixing => GetDeficitFixingRecommendations(trends, count),
			_ => new List<MapRecommendation>()
		};

		batch.Recommendations = recommendations;
		batch.Summary = GenerateBatchSummary(batch);
		batch.TotalMapsConsidered = _mapsDatabase.Get4KMapCount();

		return batch;
	}

	/// <summary>
	/// Gets recommendations focusing on a specific skillset.
	/// </summary>
	private List<MapRecommendation> GetSkillsetRecommendations(
		SkillsTrendResult trends,
		string? skillset,
		int count)
	{
		var recommendations = new List<MapRecommendation>();

		if (string.IsNullOrEmpty(skillset))
		{
			// Pick the player's strongest skillset by default
			var strongest = trends.GetStrongestSkillsets(1).FirstOrDefault() ?? "stream";
			skillset = strongest;
		}

		// Get player's skill level for this skillset
		var playerSkill =
			trends.CurrentSkillLevels.GetValueOrDefault(skillset.ToLowerInvariant(), trends.OverallSkillLevel);

		// Find maps in optimal range for this skillset
		var mmrResults = _mmrCalculator.FindMapsInOptimalRange(
			trends,
			1.0,
			0.2,
			skillset,
			count * 2
		);

		foreach (var result in mmrResults.Take(count))
		{
			var rec = new MapRecommendation
			{
				BeatmapPath = result.Map.BeatmapPath,
				Map = result.Map,
				Focus = RecommendationFocus.Skillset,
				TargetSkillset = skillset,
				Mmr = result.Mmr,
				RelativeDifficulty = result.RelativeDifficulty,
				Confidence = result.Confidence,
				Reasoning = $"Good {skillset} practice at your level ({result.Map.OverallMsd:F1} MSD)"
			};

			// Rate changes disabled for now
			// var targetMsd = (float)playerSkill;
			// var suggestedRate = _mmrCalculator.GetSuggestedRate(result.Map, targetMsd);
			// if (suggestedRate.HasValue && Math.Abs(suggestedRate.Value - 1.0f) > 0.05f)
			// {
			//     rec.SuggestedRate = suggestedRate;
			//     rec.Reasoning = $"Good {skillset} practice at {suggestedRate:0.0#}x ({result.Map.OverallMsd:F1} -> ~{targetMsd:F1} MSD)";
			// }

			recommendations.Add(rec);
		}

		return recommendations;
	}

	/// <summary>
	/// Gets recommendations for improving consistency/accuracy.
	/// </summary>
	private List<MapRecommendation> GetConsistencyRecommendations(SkillsTrendResult trends, int count)
	{
		var recommendations = new List<MapRecommendation>();

		// For consistency, recommend maps at or slightly below skill level
		// that the player has played before with room for improvement
		var mmrResults = _mmrCalculator.FindMapsInOptimalRange(
			trends,
			0.9, // Slightly below skill level
			0.15,
			limit: count * 3
		);

		// Prioritize maps the player has played but hasn't perfected
		var played = mmrResults
			.Where(r => r.Map.PlayCount > 0 && r.Map.BestPlayerAccuracy < 98)
			.OrderByDescending(r => r.Map.BestPlayerAccuracy)
			.ThenBy(r => r.RelativeDifficulty)
			.Take(count / 2)
			.ToList();

		// Also include some unplayed maps at appropriate difficulty
		var unplayed = mmrResults
			.Where(r => r.Map.PlayCount == 0)
			.OrderBy(r => Math.Abs(r.RelativeDifficulty - 0.9))
			.Take(count - played.Count)
			.ToList();

		foreach (var result in played)
			recommendations.Add(new MapRecommendation
			{
				BeatmapPath = result.Map.BeatmapPath,
				Map = result.Map,
				Focus = RecommendationFocus.Consistency,
				Mmr = result.Mmr,
				RelativeDifficulty = result.RelativeDifficulty,
				Confidence = result.Confidence,
				Reasoning =
					$"Room for improvement (best: {result.Map.BestPlayerAccuracy:F2}% on {result.Map.OverallMsd:F1} MSD)"
			});

		foreach (var result in unplayed)
			recommendations.Add(new MapRecommendation
			{
				BeatmapPath = result.Map.BeatmapPath,
				Map = result.Map,
				Focus = RecommendationFocus.Consistency,
				Mmr = result.Mmr,
				RelativeDifficulty = result.RelativeDifficulty,
				Confidence = result.Confidence,
				Reasoning = $"Comfortable difficulty for acc practice ({result.Map.OverallMsd:F1} MSD)"
			});

		return recommendations.Take(count).ToList();
	}

	/// <summary>
	/// Gets recommendations for pushing skill limits.
	/// </summary>
	private List<MapRecommendation> GetPushRecommendations(SkillsTrendResult trends, int count)
	{
		var recommendations = new List<MapRecommendation>();

		// For pushing, recommend maps above skill level (10-20% harder)
		var mmrResults = _mmrCalculator.FindMapsInOptimalRange(
			trends,
			1.15, // 15% above skill level
			0.1,
			limit: count * 2
		);

		// Prioritize unplayed maps for fresh challenges
		var sorted = mmrResults
			.OrderBy(r => r.Map.PlayCount)
			.ThenBy(r => Math.Abs(r.RelativeDifficulty - 1.15))
			.Take(count)
			.ToList();

		foreach (var result in sorted)
		{
			var diffPercent = (result.RelativeDifficulty - 1.0) * 100;

			var rec = new MapRecommendation
			{
				BeatmapPath = result.Map.BeatmapPath,
				Map = result.Map,
				Focus = RecommendationFocus.Push,
				Mmr = result.Mmr,
				RelativeDifficulty = result.RelativeDifficulty,
				Confidence = result.Confidence,
				Reasoning = $"Push map: {diffPercent:+0}% above current level ({result.Map.OverallMsd:F1} MSD)"
			};

			recommendations.Add(rec);
		}

		// Rate changes disabled for now - skip rate-based recommendations
		// if (recommendations.Count < count)
		// {
		//     var targetMsd = (float)(trends.OverallSkillLevel * 1.15);
		//     var additionalMaps = _mapsDatabase.SearchMaps(new MapSearchCriteria
		//     {
		//         MinMsd = (float)(trends.OverallSkillLevel * 0.8),
		//         MaxMsd = (float)(trends.OverallSkillLevel * 1.0),
		//         KeyCount = 4,
		//         Limit = count - recommendations.Count,
		//         OrderBy = MapSearchOrderBy.Random
		//     });
		//
		//     foreach (var map in additionalMaps)
		//     {
		//         var suggestedRate = _mmrCalculator.GetSuggestedRate(map, targetMsd);
		//         if (suggestedRate.HasValue && suggestedRate.Value > 1.0f)
		//         {
		//             var mmrResult = _mmrCalculator.CalculateMapMmr(map, trends);
		//             recommendations.Add(new MapRecommendation
		//             {
		//                 BeatmapPath = map.BeatmapPath,
		//                 Map = map,
		//                 Focus = RecommendationFocus.Push,
		//                 SuggestedRate = suggestedRate,
		//                 Mmr = mmrResult.Mmr,
		//                 RelativeDifficulty = mmrResult.RelativeDifficulty,
		//                 Confidence = mmrResult.Confidence,
		//                 Reasoning = $"Play at {suggestedRate:0.0#}x to push limits (~{targetMsd:F1} MSD)"
		//             });
		//         }
		//     }
		// }

		return recommendations.Take(count).ToList();
	}

	/// <summary>
	/// Gets recommendations for fixing skill deficits.
	/// </summary>
	private List<MapRecommendation> GetDeficitFixingRecommendations(SkillsTrendResult trends, int count)
	{
		var recommendations = new List<MapRecommendation>();

		// Identify weakest skillsets
		var weakestSkillsets = trends.GetWeakestSkillsets(3);

		if (weakestSkillsets.Count == 0)
			// No skill data, fall back to general recommendations
			return GetConsistencyRecommendations(trends, count);

		// Get recommendations per weak skillset
		var perSkillset = Math.Max(1, count / weakestSkillsets.Count);

		foreach (var skillset in weakestSkillsets)
		{
			var skillLevel = trends.CurrentSkillLevels.GetValueOrDefault(skillset, 0);
			var overallLevel = trends.OverallSkillLevel;
			var deficit = overallLevel - skillLevel;

			// Find maps for this skillset at slightly below overall level
			// This challenges the weak skillset without being overwhelming
			var targetDifficulty = skillLevel > 0 ? 1.1 : 0.9; // 10% above weak skill or 10% below overall

			var mmrResults = _mmrCalculator.FindMapsInOptimalRange(
				trends,
				targetDifficulty,
				0.15,
				skillset,
				perSkillset * 2
			);

			foreach (var result in mmrResults.Take(perSkillset))
			{
				var rec = new MapRecommendation
				{
					BeatmapPath = result.Map.BeatmapPath,
					Map = result.Map,
					Focus = RecommendationFocus.DeficitFixing,
					TargetSkillset = skillset,
					Mmr = result.Mmr,
					RelativeDifficulty = result.RelativeDifficulty,
					Confidence = result.Confidence,
					Reasoning = $"Practice {skillset} (deficit: {deficit:F1} MSD below average)"
				};

				// Rate changes disabled for now
				// var targetMsd = (float)(skillLevel + deficit * 0.3); // Work up gradually
				// var suggestedRate = _mmrCalculator.GetSuggestedRate(result.Map, targetMsd);
				// if (suggestedRate.HasValue && Math.Abs(suggestedRate.Value - 1.0f) > 0.05f)
				// {
				//     rec.SuggestedRate = suggestedRate;
				// }

				recommendations.Add(rec);
			}
		}

		// Shuffle to mix skillsets
		var random = new Random();
		return recommendations
			.OrderBy(_ => random.Next())
			.Take(count)
			.ToList();
	}

	/// <summary>
	/// Generates a summary for a recommendation batch.
	/// </summary>
	private static string GenerateBatchSummary(RecommendationBatch batch)
	{
		var focus = batch.Focus switch
		{
			RecommendationFocus.Skillset => batch.TargetSkillset != null
				? $"{batch.TargetSkillset} practice"
				: "skillset-focused",
			RecommendationFocus.Consistency => "consistency/accuracy improvement",
			RecommendationFocus.Push => "pushing limits",
			RecommendationFocus.DeficitFixing => "fixing weak skillsets",
			_ => "general"
		};

		return $"Found {batch.Recommendations.Count} maps for {focus} at skill level {batch.PlayerSkillLevel:F1}";
	}

	/// <summary>
	/// Gets all available skillsets for the skillset focus dropdown.
	/// </summary>
	public static List<string> GetAvailableSkillsets()
	{
		return new List<string>
		{
			"stream",
			"jumpstream",
			"handstream",
			"stamina",
			"jackspeed",
			"chordjack",
			"technical"
		};
	}
}
