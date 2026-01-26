using Companella.Models.Beatmap;
using Companella.Models.Difficulty;

namespace Companella.Services.Beatmap;

/// <summary>
/// Converts BPM analysis results into osu! timing points.
/// </summary>
public class TimingPointConverter
{
	/// <summary>
	/// Converts BPM analysis result into timing points.
	/// Creates a new timing point whenever the BPM changes from the previous value.
	/// </summary>
	public static List<TimingPoint> Convert(BpmResult bpmResult, int meter = 4)
	{
		var timingPoints = new List<TimingPoint>();

		if (bpmResult.Beats.Count == 0)
			return timingPoints;

		double? previousBpm = null;

		foreach (var beat in bpmResult.Beats)
		{
			var timingPoint = TimingPoint.FromBpm(beat.TimeMs, beat.Bpm, meter);
			timingPoints.Add(timingPoint);
			previousBpm = beat.Bpm;
		}

		return timingPoints;
	}

	/// <summary>
	/// Checks if two BPM values match (any difference triggers a new timing point).
	/// </summary>
	private static bool BpmMatches(double bpm1, double bpm2)
	{
		// Compare with small epsilon for floating point precision
		return Math.Abs(bpm1 - bpm2) < 0.001;
	}

	/// <summary>
	/// Converts BPM analysis result into timing points with custom defaults.
	/// </summary>
	public static List<TimingPoint> Convert(BpmResult bpmResult, TimingPoint defaults)
	{
		var timingPoints = new List<TimingPoint>();

		if (bpmResult.Beats.Count == 0)
			return timingPoints;

		double? previousBpm = null;

		foreach (var beat in bpmResult.Beats)
			// Create timing point if BPM differs from previous
			if (!previousBpm.HasValue || !BpmMatches(previousBpm.Value, beat.Bpm))
			{
				var timingPoint = new TimingPoint
				{
					Time = beat.TimeMs,
					BeatLength = 60000.0 / beat.Bpm,
					Meter = defaults.Meter,
					SampleSet = defaults.SampleSet,
					SampleIndex = defaults.SampleIndex,
					Volume = defaults.Volume,
					Uninherited = true,
					Effects = defaults.Effects
				};
				timingPoints.Add(timingPoint);
				previousBpm = beat.Bpm;
			}

		return timingPoints;
	}

	/// <summary>
	/// Gets statistics about the conversion.
	/// </summary>
	public static ConversionStats GetStats(BpmResult bpmResult, List<TimingPoint> timingPoints)
	{
		return new ConversionStats
		{
			TotalBeats = bpmResult.Beats.Count,
			TimingPointsCreated = timingPoints.Count,
			MinBpm = bpmResult.Beats.Count > 0 ? bpmResult.Beats.Min(b => b.Bpm) : 0,
			MaxBpm = bpmResult.Beats.Count > 0 ? bpmResult.Beats.Max(b => b.Bpm) : 0,
			AverageBpm = bpmResult.AverageBpm ?? (bpmResult.Beats.Count > 0 ? bpmResult.Beats.Average(b => b.Bpm) : 0)
		};
	}
}

/// <summary>
/// Statistics about a BPM to timing point conversion.
/// </summary>
public class ConversionStats
{
	public int TotalBeats { get; set; }
	public int TimingPointsCreated { get; set; }
	public double MinBpm { get; set; }
	public double MaxBpm { get; set; }
	public double AverageBpm { get; set; }
}
