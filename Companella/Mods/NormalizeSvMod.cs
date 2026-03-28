using Companella.Models.Beatmap;
using Companella.Services.Beatmap;

namespace Companella.Mods;

/// <summary>
/// Applies the same SV normalization as the Mapping tab (Normalize SV), writing a new difficulty
/// with a mod suffix on Version and filename.
/// </summary>
public class NormalizeSvMod : BaseMod
{
	public override string Name => "No SV";
	public override string Description => "Remove all scroll velocity changes";
	public override string Category => "Mapping";
	public override string Icon => "NSV";

	protected override ModResult ApplyInternal(ModContext context)
	{
		var existingTimingPoints = context.TimingPoints;
		var uninheritedCount = existingTimingPoints.Count(tp => tp.Uninherited);

		if (uninheritedCount <= 1)
			return ModResult.Failed("SV normalization needs at least two BPM (red line) sections.");

		double? mapEndTime = null;
		if (context.HitObjects.Count > 0)
			mapEndTime = context.HitObjects.Max(h => h.EndTime);

		var normalizedTimingPoints = SvNormalizer.Normalize(existingTimingPoints, null, mapEndTime);

		var uninherited = existingTimingPoints.Where(tp => tp.Uninherited).OrderBy(tp => tp.Time).ToList();
		var baseBpm = uninherited[0].Bpm;
		if (uninherited.Count > 1)
		{
			var bpmDurations = new Dictionary<double, double>();
			for (var i = 0; i < uninherited.Count; i++)
			{
				var bpm = Math.Round(uninherited[i].Bpm, 1);
				double endTime;
				if (i < uninherited.Count - 1)
					endTime = uninherited[i + 1].Time;
				else
					endTime = mapEndTime.HasValue && mapEndTime.Value > uninherited[i].Time
						? mapEndTime.Value
						: uninherited[i].Time + 60000;

				var duration = endTime - uninherited[i].Time;
				if (!bpmDurations.ContainsKey(bpm))
					bpmDurations[bpm] = 0;
				bpmDurations[bpm] += duration;
			}

			baseBpm = bpmDurations.OrderByDescending(kvp => kvp.Value).First().Key;
		}

		var svStats = SvNormalizer.GetStats(existingTimingPoints, normalizedTimingPoints, baseBpm);

		var modStats = new ModStatistics
		{
			OriginalNoteCount = context.HitObjects.Count,
			ModifiedNoteCount = context.HitObjects.Count,
			CustomStats =
			{
				["Base BPM"] = $"{baseBpm:F0}",
				["SV range"] = $"{svStats.MinSv:F2}x - {svStats.MaxSv:F2}x"
			}
		};

		return ModResult.Succeeded(context.HitObjects, modStats, normalizedTimingPoints);
	}
}
