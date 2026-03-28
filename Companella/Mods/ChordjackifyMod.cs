using Companella.Models.Beatmap;

namespace Companella.Mods;

/// <summary>
/// Snaps notes to 1/4, removes stacked duplicates, then shuffles column assignments per chord row deterministically.
/// </summary>
public class ChordjackifyMod : BaseMod
{
	public override string Name => "Chordjackify";
	public override string Description => "Snap to 1/4, dedupe, shuffle chords (deterministic)";
	public override string Category => "General";
	public override string Icon => "CJK";

	protected override ModResult ApplyInternal(ModContext context)
	{
		var modified = CloneHitObjects(context.HitObjects);

		foreach (var ho in modified)
		{
			ho.Time = SnapToNearestQuarter(ho.Time, context);
			if (ho.IsHold)
			{
				ho.EndTime = SnapToNearestQuarter(ho.EndTime, context);
				if (ho.EndTime <= ho.Time)
					ho.EndTime = ho.Time + 1;
			}
			else
			{
				ho.EndTime = ho.Time;
			}
		}

		modified = modified
			.OrderBy(h => h.Time)
			.ThenBy(h => h.Column)
			.GroupBy(h => ((long)Math.Round(h.Time), h.Column))
			.Select(g => g.First())
			.ToList();

		var byRow = modified
			.GroupBy(h => (long)Math.Round(h.Time))
			.ToList();

		foreach (var row in byRow)
		{
			var notes = row.OrderBy(h => h.Column).ToList();
			if (notes.Count <= 1)
				continue;

			var columns = notes.Select(n => n.Column).ToList();
			DeterministicShuffle(columns, SeedForRow(row.Key));

			for (var i = 0; i < notes.Count; i++)
				notes[i].Column = columns[i];
		}

		modified = modified
			.OrderBy(h => h.Time)
			.ThenBy(h => h.Column)
			.ToList();

		EnforceMinimumQuarterHoldLength(modified, context);

		modified = modified
			.OrderBy(h => h.Time)
			.ThenBy(h => h.Column)
			.ToList();

		var stats = CalculateStatistics(context.HitObjects, modified);
		return ModResult.Succeeded(modified, stats);
	}

	private static double SnapToNearestQuarter(double time, ModContext context)
	{
		var beatLen = context.GetBeatLengthAtTime(time);
		if (beatLen <= 0)
			return time;

		var step = beatLen / 4.0;
		var origin = GetSnapOrigin(time, context);
		var rel = time - origin;
		return origin + Math.Round(rel / step) * step;
	}

	/// <summary>
	/// Hold notes must span at least one 1/4 snap; if the next note in-column is too soon, demote to circle.
	/// </summary>
	private static void EnforceMinimumQuarterHoldLength(List<HitObject> hits, ModContext context)
	{
		foreach (var columnNotes in hits.GroupBy(h => h.Column))
		{
			var ordered = columnNotes.OrderBy(h => h.Time).ThenBy(h => h.Column).ToList();
			for (var i = 0; i < ordered.Count; i++)
			{
				var ho = ordered[i];
				if (!ho.IsHold)
					continue;

				var minLen = context.GetSnapDuration(ho.Time, 4);
				var nextStart = i + 1 < ordered.Count ? ordered[i + 1].Time : (double?)null;
				var canFitMin = !nextStart.HasValue || ho.Time + minLen < nextStart.Value;

				void DemoteToCircle()
				{
					ho.Type = HitObjectType.Circle;
					ho.EndTime = ho.Time;
				}

				if (ho.EndTime - ho.Time >= minLen)
				{
					if (nextStart.HasValue && ho.EndTime >= nextStart.Value)
					{
						if (canFitMin)
							ho.EndTime = ho.Time + minLen;
						else
							DemoteToCircle();
					}

					continue;
				}

				if (canFitMin)
					ho.EndTime = ho.Time + minLen;
				else
					DemoteToCircle();
			}
		}
	}

	private static double GetSnapOrigin(double time, ModContext context)
	{
		var tp = context.TimingPoints
			.Where(t => t.Uninherited && t.Time <= time)
			.OrderByDescending(t => t.Time)
			.FirstOrDefault();

		if (tp != null)
			return tp.Time;

		var first = context.TimingPoints
			.Where(t => t.Uninherited)
			.OrderBy(t => t.Time)
			.FirstOrDefault();

		return first?.Time ?? 0;
	}

	private static int SeedForRow(long rowTimeMs)
	{
		unchecked
		{
			// Constant is > long.MaxValue as unsigned; cast so multiply is long * long.
			var mix = (long)0x9E3779B97F4A7C15UL;
			var x = rowTimeMs * mix;
			return (int)(x ^ (x >> 32) ^ (x >> 48));
		}
	}

	private static void DeterministicShuffle(IList<int> list, int seed)
	{
		var rng = new Random(seed);
		for (var i = list.Count - 1; i > 0; i--)
		{
			var j = rng.Next(i + 1);
			(list[i], list[j]) = (list[j], list[i]);
		}
	}
}
