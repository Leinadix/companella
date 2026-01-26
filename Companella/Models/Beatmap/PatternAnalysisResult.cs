namespace Companella.Models.Beatmap;

/// <summary>
/// Contains all detected patterns from a beatmap analysis.
/// </summary>
public class PatternAnalysisResult
{
	/// <summary>
	/// Maximum number of top patterns to display.
	/// </summary>
	public const int MaxTopPatterns = 5;

	/// <summary>
	/// All detected patterns grouped by type.
	/// </summary>
	public Dictionary<PatternType, List<Pattern>> Patterns { get; set; } = new();

	/// <summary>
	/// Total number of patterns detected.
	/// </summary>
	public int TotalPatterns => Patterns.Values.Sum(p => p.Count);

	/// <summary>
	/// Total number of hit objects analyzed.
	/// </summary>
	public int TotalNotes { get; set; }

	/// <summary>
	/// Analysis duration in milliseconds.
	/// </summary>
	public double AnalysisDurationMs { get; set; }

	/// <summary>
	/// Whether the analysis was successful.
	/// </summary>
	public bool Success { get; set; } = true;

	/// <summary>
	/// Error message if analysis failed.
	/// </summary>
	public string? ErrorMessage { get; set; }

	/// <summary>
	/// Gets the top 5 pattern type-BPM pairs by percentage of map.
	/// Priority is based on note count within patterns (higher % = higher priority).
	/// </summary>
	public List<TopPattern> GetTopPatterns()
	{
		return GetAllPatternsSorted()
			.Take(MaxTopPatterns)
			.ToList();
	}

	/// <summary>
	/// Gets ALL pattern type-BPM pairs sorted by percentage of map.
	/// </summary>
	public List<TopPattern> GetAllPatternsSorted()
	{
		var allPatterns = new List<TopPattern>();

		if (TotalNotes == 0 || Patterns.Count == 0)
			return allPatterns;

		// Flatten all patterns and calculate percentage for each type
		foreach (var kvp in Patterns)
		{
			if (kvp.Value.Count == 0)
				continue;

			var patternType = kvp.Key;
			var patterns = kvp.Value;

			// Calculate total notes in this pattern type
			var notesInPattern = patterns.Sum(p => p.NoteCount);
			var percentage = notesInPattern / (double)TotalNotes * 100.0;

			// Get the dominant BPM (weighted average by note count)
			var totalWeightedBpm = patterns.Sum(p => p.Bpm * p.NoteCount);
			var dominantBpm = notesInPattern > 0 ? totalWeightedBpm / notesInPattern : 0;

			// Skip patterns with 0 BPM (single chord events like Jump, Hand, Quad)
			// unless they make up a significant portion
			if (dominantBpm <= 0 && percentage < 5.0)
				continue;

			allPatterns.Add(new TopPattern
			{
				Type = patternType,
				Bpm = dominantBpm,
				Percentage = percentage,
				NoteCount = notesInPattern
			});
		}

		// Sort by percentage descending
		return allPatterns
			.OrderByDescending(p => p.Percentage)
			.ToList();
	}

	/// <summary>
	/// Gets the count of a specific pattern type.
	/// </summary>
	public int GetPatternCount(PatternType type)
	{
		return Patterns.TryGetValue(type, out var list) ? list.Count : 0;
	}

	/// <summary>
	/// Gets the BPM range for a specific pattern type.
	/// </summary>
	public (double Min, double Max) GetBpmRange(PatternType type)
	{
		if (!Patterns.TryGetValue(type, out var list) || list.Count == 0)
			return (0, 0);

		var nonZeroBpms = list.Where(p => p.Bpm > 0).Select(p => p.Bpm).ToList();
		if (nonZeroBpms.Count == 0)
			return (0, 0);

		return (nonZeroBpms.Min(), nonZeroBpms.Max());
	}

	/// <summary>
	/// Gets the dominant pattern type (highest percentage of map).
	/// </summary>
	public PatternType? GetDominantPattern()
	{
		var top = GetTopPatterns();
		return top.Count > 0 ? top[0].Type : null;
	}

	/// <summary>
	/// Creates a failed result with an error message.
	/// </summary>
	public static PatternAnalysisResult Failed(string errorMessage)
	{
		return new PatternAnalysisResult
		{
			Success = false,
			ErrorMessage = errorMessage
		};
	}
}

/// <summary>
/// Represents a top pattern type-BPM pair for display.
/// </summary>
public class TopPattern
{
	/// <summary>
	/// The pattern type.
	/// </summary>
	public PatternType Type { get; set; }

	/// <summary>
	/// The dominant BPM for this pattern type (weighted average by note count).
	/// </summary>
	public double Bpm { get; set; }

	/// <summary>
	/// Percentage of map notes that are part of this pattern type.
	/// </summary>
	public double Percentage { get; set; }

	/// <summary>
	/// Total notes in all instances of this pattern type.
	/// </summary>
	public int NoteCount { get; set; }

	/// <summary>
	/// Gets the short name for display.
	/// </summary>
	public string ShortName => Type switch
	{
		PatternType.Trill => "Trill",
		PatternType.Jack => "Jack",
		PatternType.Minijack => "Minijack",
		PatternType.Stream => "Stream",
		PatternType.Jump => "Jump",
		PatternType.Hand => "Hand",
		PatternType.Quad => "Quad",
		PatternType.Jumpstream => "JS",
		PatternType.Handstream => "HS",
		PatternType.Chordjack => "CJ",
		PatternType.Roll => "Roll",
		PatternType.Bracket => "Bracket",
		PatternType.Jumptrill => "JT",
		_ => Type.ToString()
	};

	/// <summary>
	/// Gets the BPM display string.
	/// Returns "-" for patterns with no inherent BPM.
	/// </summary>
	public string BpmDisplay => Bpm > 0 ? $"{Bpm:F0}" : "-";

	/// <summary>
	/// Gets the percentage display string.
	/// </summary>
	public string PercentageDisplay => $"{Percentage:F0}%";

	/// <summary>
	/// Gets a compact display string: "Type @ BPM (%)".
	/// </summary>
	public string CompactDisplay => Bpm > 0
		? $"{ShortName} @ {Bpm:F0}"
		: ShortName;
}
