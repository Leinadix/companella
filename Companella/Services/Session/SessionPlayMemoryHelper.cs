using OsuMemoryDataProvider;
using OsuMemoryDataProvider.OsuMemoryModels.Direct;

namespace Companella.Services.Session;

/// <summary>
/// Helpers for reading play stats from osu! memory when direct accuracy is unavailable.
/// </summary>
internal static class SessionPlayMemoryHelper
{
	internal readonly record struct PlayStats(double Accuracy, int Misses, int TotalHits, int Score);

	internal static PlayStats ReadPlayStats(StructuredOsuMemoryReader memoryReader)
	{
		var player = new Player();
		if (!memoryReader.TryRead(player))
			return ReadFromResultsScreen(memoryReader);

		return BuildStats(player);
	}

	private static PlayStats ReadFromResultsScreen(StructuredOsuMemoryReader memoryReader)
	{
		var resultsScreen = new ResultsScreen();
		if (!memoryReader.TryRead(resultsScreen))
			return default;

		return BuildStats(resultsScreen);
	}

	private static PlayStats BuildStats(OsuMemoryDataProvider.OsuMemoryModels.Abstract.RulesetPlayData data)
	{
		var misses = data.HitMiss;
		var totalHits = data.Hit300 + data.Hit100 + data.Hit50 + data.HitGeki + data.HitKatu + misses;
		var accuracy = data is Player player ? player.Accuracy : 0;

		if (accuracy <= 0 && totalHits > 0)
			accuracy = ComputeManiaAccuracy(data.HitGeki, data.Hit300, data.HitKatu, data.Hit100, data.Hit50, misses);

		return new PlayStats(accuracy, misses, totalHits, data.Score);
	}

	/// <summary>
	/// Computes osu!mania v1 accuracy from judgement counts.
	/// </summary>
	internal static double ComputeManiaAccuracy(int hitGeki, int hit300, int hitKatu, int hit100, int hit50,
		int hitMiss)
	{
		var total = hitGeki + hit300 + hitKatu + hit100 + hit50 + hitMiss;
		if (total == 0)
			return 0;

		var score = (hitGeki + hit300) * 300.0 + hitKatu * 200.0 + hit100 * 100.0 + hit50 * 50.0;
		return score / (total * 300.0) * 100.0;
	}
}
