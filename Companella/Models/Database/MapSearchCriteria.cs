namespace Companella.Models.Database;

/// <summary>
/// Criteria for searching maps in the database.
/// </summary>
public class MapSearchCriteria
{
	/// <summary>
	/// Filter by dominant skillset (null = any).
	/// </summary>
	public string? Skillset { get; set; }

	/// <summary>
	/// Minimum MSD value (at 1.0x rate).
	/// </summary>
	public float? MinMsd { get; set; }

	/// <summary>
	/// Maximum MSD value (at 1.0x rate).
	/// </summary>
	public float? MaxMsd { get; set; }

	/// <summary>
	/// Filter by key count (null = any, typically 4 for 4K).
	/// </summary>
	public int? KeyCount { get; set; }

	/// <summary>
	/// Only include maps that have been played.
	/// </summary>
	public bool OnlyPlayed { get; set; }

	/// <summary>
	/// Only include maps that have NOT been played.
	/// </summary>
	public bool OnlyUnplayed { get; set; }

	/// <summary>
	/// Minimum player accuracy on the map (requires OnlyPlayed = true).
	/// </summary>
	public double? MinPlayerAccuracy { get; set; }

	/// <summary>
	/// Maximum player accuracy on the map (requires OnlyPlayed = true).
	/// </summary>
	public double? MaxPlayerAccuracy { get; set; }

	/// <summary>
	/// Text to search in title, artist, or creator.
	/// </summary>
	public string? SearchText { get; set; }

	/// <summary>
	/// Maximum number of results to return.
	/// </summary>
	public int? Limit { get; set; }

	/// <summary>
	/// Offset for pagination.
	/// </summary>
	public int? Offset { get; set; }

	/// <summary>
	/// Order by field.
	/// </summary>
	public MapSearchOrderBy OrderBy { get; set; } = MapSearchOrderBy.OverallMsd;

	/// <summary>
	/// Order direction.
	/// </summary>
	public bool Ascending { get; set; }

	/// <summary>
	/// Creates criteria for finding maps in a specific MSD range.
	/// </summary>
	public static MapSearchCriteria ForMsdRange(float minMsd, float maxMsd, int? keyCount = 4)
	{
		return new MapSearchCriteria
		{
			MinMsd = minMsd,
			MaxMsd = maxMsd,
			KeyCount = keyCount
		};
	}

	/// <summary>
	/// Creates criteria for finding maps with a specific dominant skillset.
	/// </summary>
	public static MapSearchCriteria ForSkillset(string skillset, float? minMsd = null, float? maxMsd = null)
	{
		return new MapSearchCriteria
		{
			Skillset = skillset,
			MinMsd = minMsd,
			MaxMsd = maxMsd,
			KeyCount = 4
		};
	}

	/// <summary>
	/// Creates criteria for finding unplayed maps in a MSD range.
	/// </summary>
	public static MapSearchCriteria ForUnplayedInRange(float minMsd, float maxMsd)
	{
		return new MapSearchCriteria
		{
			MinMsd = minMsd,
			MaxMsd = maxMsd,
			OnlyUnplayed = true,
			KeyCount = 4
		};
	}
}

/// <summary>
/// Fields to order map search results by.
/// </summary>
public enum MapSearchOrderBy
{
	/// <summary>
	/// Order by overall MSD.
	/// </summary>
	OverallMsd,

	/// <summary>
	/// Order by title.
	/// </summary>
	Title,

	/// <summary>
	/// Order by artist.
	/// </summary>
	Artist,

	/// <summary>
	/// Order by last analyzed time.
	/// </summary>
	LastAnalyzed,

	/// <summary>
	/// Order by play count.
	/// </summary>
	PlayCount,

	/// <summary>
	/// Order by best player accuracy.
	/// </summary>
	BestAccuracy,

	/// <summary>
	/// Order randomly.
	/// </summary>
	Random
}
