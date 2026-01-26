using Companella.Models.Beatmap;

namespace Companella.Mods;

/// <summary>
/// Context passed to mods containing all data needed for modification.
/// </summary>
public class ModContext
{
	/// <summary>
	/// The source osu file being modified.
	/// </summary>
	public OsuFile SourceFile { get; }

	/// <summary>
	/// The parsed hit objects from the source file.
	/// </summary>
	public List<HitObject> HitObjects { get; }

	/// <summary>
	/// The key count (CircleSize) of the beatmap.
	/// </summary>
	public int KeyCount { get; }

	/// <summary>
	/// The timing points from the source file.
	/// </summary>
	public List<TimingPoint> TimingPoints { get; }

	/// <summary>
	/// Creates a new mod context.
	/// </summary>
	/// <param name="sourceFile">The source osu file.</param>
	/// <param name="hitObjects">The parsed hit objects.</param>
	/// <param name="keyCount">The key count of the beatmap.</param>
	/// <param name="timingPoints">The timing points from the beatmap.</param>
	public ModContext(OsuFile sourceFile, List<HitObject> hitObjects, int keyCount, List<TimingPoint> timingPoints)
	{
		SourceFile = sourceFile ?? throw new ArgumentNullException(nameof(sourceFile));
		HitObjects = hitObjects ?? throw new ArgumentNullException(nameof(hitObjects));
		KeyCount = keyCount;
		TimingPoints = timingPoints ?? throw new ArgumentNullException(nameof(timingPoints));
	}

	/// <summary>
	/// Creates a new mod context from an OsuFile.
	/// </summary>
	/// <param name="sourceFile">The source osu file.</param>
	/// <param name="hitObjects">The parsed hit objects.</param>
	public ModContext(OsuFile sourceFile, List<HitObject> hitObjects)
		: this(sourceFile, hitObjects, (int)sourceFile.CircleSize, sourceFile.TimingPoints)
	{
	}

	/// <summary>
	/// Gets the BPM at a specific time in the beatmap.
	/// </summary>
	/// <param name="time">The time in milliseconds.</param>
	/// <returns>The BPM at that time, or 120 if no timing point is found.</returns>
	public double GetBpmAtTime(double time)
	{
		var uninherited = TimingPoints
			.Where(tp => tp.Uninherited && tp.Time <= time)
			.OrderByDescending(tp => tp.Time)
			.FirstOrDefault();

		return uninherited?.Bpm ?? 120.0;
	}

	/// <summary>
	/// Gets the beat length (ms per beat) at a specific time in the beatmap.
	/// </summary>
	/// <param name="time">The time in milliseconds.</param>
	/// <returns>The beat length at that time, or 500ms (120 BPM) if no timing point is found.</returns>
	public double GetBeatLengthAtTime(double time)
	{
		var uninherited = TimingPoints
			.Where(tp => tp.Uninherited && tp.Time <= time)
			.OrderByDescending(tp => tp.Time)
			.FirstOrDefault();

		return uninherited?.BeatLength ?? 500.0;
	}

	/// <summary>
	/// Calculates the duration for a given snap divisor at a specific time.
	/// </summary>
	/// <param name="time">The time in milliseconds.</param>
	/// <param name="snapDivisor">The snap divisor (e.g., 4 for 1/4, 8 for 1/8).</param>
	/// <returns>The duration in milliseconds for the given snap.</returns>
	public double GetSnapDuration(double time, int snapDivisor)
	{
		var beatLength = GetBeatLengthAtTime(time);
		return beatLength / snapDivisor;
	}
}
