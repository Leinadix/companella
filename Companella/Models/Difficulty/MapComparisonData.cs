using Companella.Models.Beatmap;

namespace Companella.Models.Difficulty;

/// <summary>
/// Aggregates MSD scores and pattern analysis for map comparison and classification.
/// </summary>
public class MapComparisonData
{
	/// <summary>
	/// MSD skillset scores from the analyzer.
	/// </summary>
	public SkillsetScores? MsdScores { get; set; }

	/// <summary>
	/// Pattern analysis results.
	/// </summary>
	public PatternAnalysisResult? PatternResult { get; set; }

	/// <summary>
	/// Reference to the source OsuFile.
	/// </summary>
	public OsuFile? OsuFile { get; set; }

	/// <summary>
	/// Overall MSD difficulty (at 1.0x rate).
	/// </summary>
	public float OverallMsd => MsdScores?.Overall ?? 0;

	/// <summary>
	/// Whether MSD data is available.
	/// </summary>
	public bool HasMsdData => MsdScores != null;

	/// <summary>
	/// Whether pattern data is available.
	/// </summary>
	public bool HasPatternData => PatternResult != null && PatternResult.Success;

	/// <summary>
	/// Gets the dominant MSD skillset.
	/// </summary>
	public (string Name, float Value)? DominantSkillset => MsdScores?.GetDominantSkillset();

	/// <summary>
	/// Gets the dominant pattern type.
	/// </summary>
	public PatternType? DominantPattern => PatternResult?.GetDominantPattern();

	/// <summary>
	/// Gets the peak BPM across all patterns.
	/// </summary>
	public double PeakPatternBpm
	{
		get
		{
			if (PatternResult == null || PatternResult.Patterns.Count == 0)
				return 0;

			return PatternResult.Patterns.Values
				.SelectMany(p => p)
				.Select(p => p.Bpm)
				.DefaultIfEmpty(0)
				.Max();
		}
	}

	/// <summary>
	/// Gets the average BPM across all patterns.
	/// </summary>
	public double AveragePatternBpm
	{
		get
		{
			if (PatternResult == null || PatternResult.Patterns.Count == 0)
				return 0;

			var allBpms = PatternResult.Patterns.Values
				.SelectMany(p => p)
				.Select(p => p.Bpm)
				.ToList();

			return allBpms.Count > 0 ? allBpms.Average() : 0;
		}
	}

	/// <summary>
	/// Gets the total pattern count.
	/// </summary>
	public int TotalPatternCount => PatternResult?.TotalPatterns ?? 0;

	/// <summary>
	/// Gets the total note count.
	/// </summary>
	public int TotalNoteCount => PatternResult?.TotalNotes ?? 0;

	/// <summary>
	/// Gets pattern density (patterns per 1000 notes).
	/// </summary>
	public double PatternDensity
	{
		get
		{
			if (TotalNoteCount == 0)
				return 0;
			return TotalPatternCount / (double)TotalNoteCount * 1000.0;
		}
	}

	/// <summary>
	/// Gets the MSD difficulty spread (difference between highest and lowest skillset).
	/// Higher spread indicates more specialized map.
	/// </summary>
	public float MsdSpread
	{
		get
		{
			if (MsdScores == null)
				return 0;

			var skillsets = new[]
			{
				MsdScores.Stream,
				MsdScores.Jumpstream,
				MsdScores.Handstream,
				MsdScores.Stamina,
				MsdScores.Jackspeed,
				MsdScores.Chordjack,
				MsdScores.Technical
			};

			return skillsets.Max() - skillsets.Min();
		}
	}

	/// <summary>
	/// Gets a complexity score based on pattern variety and MSD spread.
	/// </summary>
	public double ComplexityScore
	{
		get
		{
			// Number of different pattern types present
			var patternVariety = PatternResult?.Patterns.Count(p => p.Value.Count > 0) ?? 0;

			// Normalize pattern variety (0-1, assuming max 13 pattern types)
			var varietyScore = patternVariety / 13.0;

			// Normalize MSD spread (0-1, assuming max spread of 20)
			var spreadScore = Math.Min(MsdSpread / 20.0, 1.0);

			// Combine with weights
			return varietyScore * 0.6 + spreadScore * 0.4;
		}
	}

	/// <summary>
	/// Gets pattern counts by type for quick access.
	/// </summary>
	public Dictionary<PatternType, int> PatternCounts
	{
		get
		{
			var counts = new Dictionary<PatternType, int>();
			if (PatternResult?.Patterns != null)
				foreach (var kvp in PatternResult.Patterns)
					counts[kvp.Key] = kvp.Value.Count;

			return counts;
		}
	}

	/// <summary>
	/// Gets the BPM-weighted difficulty for a specific pattern type.
	/// Higher BPM patterns contribute more to difficulty.
	/// </summary>
	public double GetPatternBpmDifficulty(PatternType type)
	{
		if (PatternResult == null || !PatternResult.Patterns.TryGetValue(type, out var patterns))
			return 0;

		if (patterns.Count == 0)
			return 0;

		// Weight by BPM (normalized to 200 BPM baseline)
		return patterns.Sum(p => p.Bpm / 200.0 * p.NoteCount);
	}

	/// <summary>
	/// Gets a summary dictionary of all metrics.
	/// </summary>
	public Dictionary<string, object> GetMetricsSummary()
	{
		return new Dictionary<string, object>
		{
			{ "OverallMsd", OverallMsd },
			{ "DominantSkillset", DominantSkillset?.Name ?? "None" },
			{ "DominantPattern", DominantPattern?.ToString() ?? "None" },
			{ "PeakBpm", PeakPatternBpm },
			{ "AverageBpm", AveragePatternBpm },
			{ "TotalPatterns", TotalPatternCount },
			{ "TotalNotes", TotalNoteCount },
			{ "PatternDensity", PatternDensity },
			{ "MsdSpread", MsdSpread },
			{ "Complexity", ComplexityScore }
		};
	}
}
