using Companella.Models.Beatmap;
using Companella.Services.Tools;

namespace Companella.Mods;

/// <summary>
/// Mirrors the entire map in time (plays backwards) and reverses the audio with ffmpeg
/// using the same anchor duration so notes stay aligned with the track.
/// </summary>
public class ReverseMod : BaseMod
{
	public override string Name => "Reverse";
	public override string Description => "Mirror hit objects and timing in time; reverse audio (ffmpeg)";
	public override string Category => "General";
	public override string Icon => "REV";

	protected override string? ValidateContext(ModContext context)
	{
		var err = base.ValidateContext(context);
		if (err != null)
			return err;

		if (!File.Exists(context.SourceFile.AudioFilePath))
			return "Reverse needs the beatmap audio file present in the song folder.";

		return null;
	}

	protected override ModResult ApplyInternal(ModContext context)
	{
		var audioMs = AudioReverseUtilities.GetAudioDurationMs(context.SourceFile.AudioFilePath);
		var mapEndMs = GetMapEndTimeMs(context);
		var anchorMs = Math.Max(audioMs, mapEndMs);
		if (anchorMs <= 0)
			return ModResult.Failed("Could not determine a positive duration to mirror the map.");

		var modifiedHits = CloneHitObjects(context.HitObjects);
		foreach (var ho in modifiedHits)
		{
			var start = ho.Time;
			var end = Math.Max(ho.Time, ho.EndTime);
			var newStart = anchorMs - end;
			var newEnd = anchorMs - start;
			ho.Time = newStart;
			if (ho.IsHold)
			{
				ho.EndTime = newEnd;
				if (ho.Time > ho.EndTime)
					(ho.Time, ho.EndTime) = (ho.EndTime, ho.Time);
			}
			else
			{
				ho.EndTime = ho.Time;
			}
		}

		modifiedHits = modifiedHits
			.OrderBy(h => h.Time)
			.ThenBy(h => h.Column)
			.ToList();

		var modifiedTiming = new List<TimingPoint>();
		foreach (var tp in context.TimingPoints)
		{
			var t = new TimingPoint
			{
				Time = anchorMs - tp.Time,
				BeatLength = tp.BeatLength,
				Meter = tp.Meter,
				SampleSet = tp.SampleSet,
				SampleIndex = tp.SampleIndex,
				Volume = tp.Volume,
				Uninherited = tp.Uninherited,
				Effects = tp.Effects
			};
			modifiedTiming.Add(t);
		}

		modifiedTiming.Sort((a, b) => a.Time.CompareTo(b.Time));

		var stats = CalculateStatistics(context.HitObjects, modifiedHits);
		stats.CustomStats["Anchor (ms)"] = $"{anchorMs:F0}";

		return ModResult.Succeeded(
			modifiedHits,
			stats,
			modifiedTiming,
			new ModAudioReverseSpec { AnchorDurationMs = anchorMs });
	}

	private static double GetMapEndTimeMs(ModContext context)
	{
		var m = 0.0;
		foreach (var h in context.HitObjects)
		{
			var e = Math.Max(h.Time, h.EndTime);
			if (e > m)
				m = e;
		}

		foreach (var tp in context.TimingPoints)
			if (tp.Time > m)
				m = tp.Time;

		return m;
	}
}
