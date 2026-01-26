using Companella.Models.Beatmap;
using Companella.Services.Common;

namespace Companella.Services.Tools;

/// <summary>
/// Applies universal offset changes to all timing points in a beatmap.
/// </summary>
public class OffsetChanger
{
	/// <summary>
	/// Applies an offset to all timing points.
	/// </summary>
	/// <param name="timingPoints">The timing points to modify.</param>
	/// <param name="offsetMs">The offset in milliseconds (positive = later, negative = earlier).</param>
	/// <returns>New list of timing points with the offset applied.</returns>
	public static List<TimingPoint> ApplyOffset(List<TimingPoint> timingPoints, double offsetMs)
	{
		if (Math.Abs(offsetMs) < 0.001)
			return timingPoints.ToList(); // No change needed

		var result = new List<TimingPoint>();

		foreach (var tp in timingPoints)
		{
			var newTp = new TimingPoint
			{
				Time = tp.Time + offsetMs,
				BeatLength = tp.BeatLength,
				Meter = tp.Meter,
				SampleSet = tp.SampleSet,
				SampleIndex = tp.SampleIndex,
				Volume = tp.Volume,
				Uninherited = tp.Uninherited,
				Effects = tp.Effects
			};
			result.Add(newTp);
		}

		Logger.Info($"[Offset] Applied {offsetMs:+0.##;-0.##;0}ms offset to {result.Count} timing points");
		return result;
	}

	/// <summary>
	/// Gets statistics about the offset change.
	/// </summary>
	public static OffsetStats GetStats(List<TimingPoint> original, List<TimingPoint> modified, double offsetMs)
	{
		return new OffsetStats
		{
			OffsetMs = offsetMs,
			TimingPointsModified = modified.Count,
			OriginalFirstTime = original.Count > 0 ? original.Min(tp => tp.Time) : 0,
			NewFirstTime = modified.Count > 0 ? modified.Min(tp => tp.Time) : 0
		};
	}
}

/// <summary>
/// Statistics about an offset change.
/// </summary>
public class OffsetStats
{
	public double OffsetMs { get; set; }
	public int TimingPointsModified { get; set; }
	public double OriginalFirstTime { get; set; }
	public double NewFirstTime { get; set; }
}
