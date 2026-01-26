using Companella.Models.Beatmap;
using Companella.Services.Common;

namespace Companella.Services.Beatmap;

/// <summary>
/// Utility service for parsing and serializing hit objects to/from .osu file format.
/// Consolidates hit object parsing logic that was previously duplicated across services.
/// </summary>
public class HitObjectSerializer
{
	/// <summary>
	/// Parses hit objects from an OsuFile's raw sections.
	/// </summary>
	/// <param name="osuFile">The osu file to parse hit objects from.</param>
	/// <returns>A list of parsed hit objects, ordered by time.</returns>
	public static List<HitObject> Parse(OsuFile osuFile)
	{
		ArgumentNullException.ThrowIfNull(osuFile);
		var hitObjects = new List<HitObject>();

		if (!osuFile.RawSections.TryGetValue("HitObjects", out var lines))
		{
			Logger.Info("[HitObjectSerializer] No HitObjects section found in osu file");
			return hitObjects;
		}

		var keyCount = (int)osuFile.CircleSize;

		foreach (var line in lines)
		{
			if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("//", StringComparison.Ordinal))
				continue;

			var hitObject = HitObject.Parse(line, keyCount);
			if (hitObject != null) hitObjects.Add(hitObject);
		}

		Logger.Info($"[HitObjectSerializer] Parsed {hitObjects.Count} hit objects ({keyCount}K)");
		return hitObjects.OrderBy(h => h.Time).ToList();
	}

	/// <summary>
	/// Parses hit objects from raw lines.
	/// </summary>
	/// <param name="lines">The raw hit object lines from a .osu file.</param>
	/// <param name="keyCount">The key count of the beatmap.</param>
	/// <returns>A list of parsed hit objects, ordered by time.</returns>
	public static List<HitObject> Parse(IEnumerable<string> lines, int keyCount)
	{
		var hitObjects = new List<HitObject>();

		foreach (var line in lines)
		{
			if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("//", StringComparison.Ordinal))
				continue;

			var hitObject = HitObject.Parse(line, keyCount);
			if (hitObject != null) hitObjects.Add(hitObject);
		}

		return hitObjects.OrderBy(h => h.Time).ToList();
	}

	/// <summary>
	/// Serializes a single hit object to .osu file format.
	/// </summary>
	/// <param name="hitObject">The hit object to serialize.</param>
	/// <param name="keyCount">The key count (used to calculate X position).</param>
	/// <returns>The .osu formatted string for this hit object.</returns>
	public static string Serialize(HitObject hitObject, int keyCount)
	{
		ArgumentNullException.ThrowIfNull(hitObject);

		return hitObject.ToOsuString(keyCount);
	}

	/// <summary>
	/// Serializes a list of hit objects to .osu file format.
	/// </summary>
	/// <param name="hitObjects">The hit objects to serialize.</param>
	/// <param name="keyCount">The key count (used to calculate X positions).</param>
	/// <returns>A list of .osu formatted strings for the hit objects.</returns>
	public static List<string> SerializeAll(List<HitObject> hitObjects, int keyCount)
	{
		ArgumentNullException.ThrowIfNull(hitObjects);

		// Sort by time before serializing
		var sorted = hitObjects.OrderBy(ho => ho.Time).ToList();

		return sorted.Select(ho => Serialize(ho, keyCount)).ToList();
	}

	/// <summary>
	/// Gets summary statistics about a list of hit objects.
	/// </summary>
	/// <param name="hitObjects">The hit objects to analyze.</param>
	/// <returns>A summary string with note counts.</returns>
	public static string GetSummary(List<HitObject> hitObjects)
	{
		if (hitObjects == null || hitObjects.Count == 0)
			return "No hit objects";

		var circles = hitObjects.Count(ho => ho.Type == HitObjectType.Circle);
		var holds = hitObjects.Count(ho => ho.Type == HitObjectType.Hold);

		return $"{hitObjects.Count} notes ({circles} circles, {holds} holds)";
	}

	/// <summary>
	/// Validates that hit objects can be serialized correctly by doing a round-trip test.
	/// </summary>
	/// <param name="hitObjects">The hit objects to validate.</param>
	/// <param name="keyCount">The key count.</param>
	/// <returns>True if all hit objects pass round-trip validation.</returns>
	public static bool ValidateRoundTrip(List<HitObject> hitObjects, int keyCount)
	{
		foreach (var original in hitObjects)
		{
			var serialized = Serialize(original, keyCount);
			var parsed = HitObject.Parse(serialized, keyCount);

			if (parsed == null)
				return false;

			// Compare key properties
			if (Math.Abs(parsed.Time - original.Time) > 1)
				return false;
			if (parsed.Column != original.Column)
				return false;
			if (parsed.Type != original.Type)
				return false;
			if (original.Type == HitObjectType.Hold && Math.Abs(parsed.EndTime - original.EndTime) > 1)
				return false;
		}

		return true;
	}
}
