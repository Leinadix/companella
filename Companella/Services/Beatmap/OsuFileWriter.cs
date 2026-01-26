using Companella.Models.Beatmap;
using System.Diagnostics.CodeAnalysis;

namespace Companella.Services.Beatmap;

/// <summary>
/// Writes modified osu! beatmap (.osu) files while preserving original structure.
/// </summary>
[SuppressMessage("", "CA1310")]
public class OsuFileWriter
{
	/// <summary>
	/// Writes the OsuFile back to disk, replacing timing points with the new ones.
	/// </summary>
	public static void Write(OsuFile osuFile, List<TimingPoint> newTimingPoints)
	{
		if (!File.Exists(osuFile.FilePath))
			throw new FileNotFoundException($"Original beatmap file not found: {osuFile.FilePath}");

		var lines = File.ReadAllLines(osuFile.FilePath).ToList();
		var result = new List<string>();

		var inTimingPointsSection = false;
		var timingPointsWritten = false;

		foreach (var line in lines)
		{
			var trimmedLine = line.Trim();

			// Check for section header
			if (trimmedLine.StartsWith('[') && trimmedLine.EndsWith(']'))
			{
				// If we were in TimingPoints section, write new timing points before leaving
				if (inTimingPointsSection && !timingPointsWritten)
				{
					WriteTimingPoints(result, newTimingPoints);
					timingPointsWritten = true;
				}

				inTimingPointsSection = trimmedLine == "[TimingPoints]";
				result.Add(line);
				continue;
			}

			// If in TimingPoints section, skip original lines (we'll write new ones)
			if (inTimingPointsSection)
				// Skip original timing point data, but keep comments
				if (!string.IsNullOrEmpty(trimmedLine) && !trimmedLine.StartsWith("//"))
					continue;

			result.Add(line);
		}

		// If file ends while still in TimingPoints section
		if (inTimingPointsSection && !timingPointsWritten) WriteTimingPoints(result, newTimingPoints);

		// Create backup before writing
		var backupPath = osuFile.FilePath + ".backup";
		File.Copy(osuFile.FilePath, backupPath, true);

		File.WriteAllLines(osuFile.FilePath, result);
	}

	/// <summary>
	/// Writes timing points with an empty line before for proper formatting.
	/// </summary>
	private static void WriteTimingPoints(List<string> result, List<TimingPoint> timingPoints)
	{
		// Sort timing points by time, then by uninherited status (uninherited first at same time)
		// This ensures proper osu! format: red lines (uninherited) come before green lines (inherited) at the same time
		var sorted = timingPoints.OrderBy(tp => tp.Time).ThenByDescending(tp => tp.Uninherited).ToList();

		foreach (var tp in sorted) result.Add(tp.ToString());
	}

	/// <summary>
	/// Merges new uninherited timing points with existing inherited ones,
	/// replacing only the uninherited (BPM) timing points.
	/// </summary>
	public static List<TimingPoint> MergeTimingPoints(List<TimingPoint> existing, List<TimingPoint> newUninherited)
	{
		// Keep only inherited timing points from existing
		var inherited = existing.Where(tp => !tp.Uninherited).ToList();

		// Combine with new uninherited timing points
		var merged = new List<TimingPoint>();
		merged.AddRange(newUninherited);
		merged.AddRange(inherited);

		// Sort by time, then by uninherited status (uninherited first at same time)
		return merged.OrderBy(tp => tp.Time).ThenByDescending(tp => tp.Uninherited).ToList();
	}
}
