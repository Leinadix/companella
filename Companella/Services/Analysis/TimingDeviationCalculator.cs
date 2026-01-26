using System.Diagnostics.CodeAnalysis;
using Companella.Models.Application;
using Companella.Models.Beatmap;
using Companella.Services.Beatmap;
using Companella.Services.Common;

namespace Companella.Services.Analysis;

/// <summary>
/// Calculates timing deviations by correlating replay key events with beatmap hit objects.
/// </summary>
public class TimingDeviationCalculator
{
	private readonly OsuFileParser _fileParser;

	public TimingDeviationCalculator(OsuFileParser fileParser)
	{
		_fileParser = fileParser;
	}

	/// <summary>
	/// Gets the miss window (early limit) for a given OD.
	/// Based on Mania-Replay-Master: you can hit a note up to missWindow ms EARLY.
	/// </summary>
	private static double GetMissWindow(double od)
	{
		// osu!mania miss window: 188 - 3 * OD (for non-ScoreV2)
		return 188.0 - 3.0 * od;
	}

	/// <summary>
	/// Gets the 100 window (late limit) for a given OD.
	/// Based on Mania-Replay-Master: for regular notes, you can only hit up to 100Window ms LATE.
	/// </summary>
	private static double Get100Window(double od)
	{
		// osu!mania 100 window: 127 - 3 * OD (for non-ScoreV2)
		return 127.0 - 3.0 * od;
	}

	/// <summary>
	/// Gets the 50 window for a given OD.
	/// </summary>
	private static double Get50Window(double od)
	{
		return 151.0 - 3.0 * od;
	}

	/// <summary>
	/// Calculates timing deviations for a replay against a beatmap.
	/// Based on Mania-Replay-Master's approach for accurate matching.
	/// </summary>
	/// <param name="beatmapPath">Path to the .osu beatmap file.</param>
	/// <param name="keyEvents">Key press events extracted from the replay.</param>
	/// <param name="rate">Rate multiplier (1.5 for DT, 0.75 for HT, 1.0 for normal).</param>
	/// <param name="mirror">Whether mirror mod is active (flips beatmap columns).</param>
	/// <param name="customOD"></param>
	/// <returns>Analysis result containing all timing deviations.</returns>
	public static TimingAnalysisResult CalculateDeviations(string beatmapPath, List<ManiaKeyEvent> keyEvents,
		float rate = 1.0f, bool mirror = false, double? customOD = null)
	{
		var result = new TimingAnalysisResult
		{
			BeatmapPath = beatmapPath,
			Rate = rate,
			HasMirror = mirror,
			OriginalKeyEvents = keyEvents // Store for re-analysis
		};

		try
		{
			// Parse the beatmap
			var osuFile = OsuFileParser.Parse(beatmapPath);

			// Verify it's a mania map
			if (osuFile.Mode != 3)
			{
				result.Success = false;
				result.ErrorMessage = "Not a mania beatmap";
				return result;
			}

			// Get key count
			var keyCount = (int)osuFile.CircleSize;
			// Use custom OD if provided, otherwise use beatmap's OD
			var od = customOD ?? osuFile.OverallDifficulty;

			// Following Mania-Replay-Master: asymmetric hit windows
			// - missWindow: how early you can hit (188 - 3*OD)
			// - window100: how late you can hit for regular notes (127 - 3*OD)
			var missWindow = GetMissWindow(od);
			var window100 = Get100Window(od);

			// Parse hit objects from the beatmap
			var hitObjects = ParseHitObjects(osuFile, keyCount);
			if (hitObjects.Count == 0)
			{
				result.Success = false;
				result.ErrorMessage = "No hit objects found in beatmap";
				return result;
			}

			// Apply mirror mod: flip beatmap columns (not replay columns!)
			// Following Mania-Replay-Master: column = keyCount - column - 1
			if (mirror)
			{
				Logger.Info($"[DeviationCalc] Applying mirror mod adjustment");
				foreach (var hitObject in hitObjects) hitObject.Column = keyCount - hitObject.Column - 1;
			}

			// Calculate map duration in song time (no rate scaling needed since all times are in song time)
			var lastObjectTime = hitObjects.Max(h => h.IsHold ? h.EndTime : h.Time);
			result.MapDuration = lastObjectTime;

			// Debug: Show raw times (no scaling needed)
			Logger.Info($"[DeviationCalc] Beatmap first note: {hitObjects.OrderBy(h => h.Time).First().Time:F0}ms");
			Logger.Info(
				$"[DeviationCalc] Replay first press: {keyEvents.Where(e => e.IsPress).OrderBy(e => e.Time).FirstOrDefault()?.Time:F0}ms");

			// IMPORTANT: Both replay times and beatmap times are in SONG TIME.
			// osu! replay frames store time relative to song position, not real elapsed time.
			// This means for DT/HT, the replay already records hits at the correct song position
			// and no scaling is needed for either the beatmap or replay times.
			// The rate is still stored for display purposes (MapDuration calculation above).
			if (rate != 1.0f)
				Logger.Info(
					$"[DeviationCalc] Rate mod detected: {rate}x (no time scaling needed - both replay and beatmap use song time)");

			// Both replay and beatmap use song time coordinates - no scaling needed
			var processedKeyEvents = keyEvents;

			// Get press and release events separately
			var pressEvents = processedKeyEvents.Where(e => e.IsPress).OrderBy(e => e.Time).ToList();
			var releaseEvents = processedKeyEvents.Where(e => !e.IsPress).OrderBy(e => e.Time).ToList();

			// Count LNs for statistics
			var lnCount = hitObjects.Count(h => h.IsHold);
			var regularNoteCount = hitObjects.Count - lnCount;

			Logger.Info(
				$"[DeviationCalc] Analyzing {hitObjects.Count} objects ({regularNoteCount} notes, {lnCount} LNs) against {pressEvents.Count} presses, {releaseEvents.Count} releases");
			Logger.Info(
				$"[DeviationCalc] OD={od}, earlyWindow(miss)={missWindow:F0}ms, lateWindow(100)={window100:F0}ms");

			// Sort hit objects by time for proper matching
			var sortedHitObjects = hitObjects.OrderBy(h => h.Time).ToList();

			// Debug: Show first few notes and keypresses (both in song time)
			Logger.Info(
				$"[DeviationCalc] First 5 notes: {string.Join(", ", sortedHitObjects.Take(5).Select(n => $"[{n.Time:F0}ms Col{n.Column}]"))}");
			Logger.Info(
				$"[DeviationCalc] First 5 replay presses: {string.Join(", ", pressEvents.Take(5).Select(p => $"[{p.Time:F0}ms Col{p.Column}]"))}");

			// Show time alignment diagnostic
			if (sortedHitObjects.Count > 0 && pressEvents.Count > 0)
			{
				var firstNote = sortedHitObjects.First();
				var firstPressInCol = pressEvents.FirstOrDefault(p => p.Column == firstNote.Column);
				if (firstPressInCol != null)
					Logger.Info(
						$"[DeviationCalc] Time alignment check: First note Col{firstNote.Column} at {firstNote.Time:F0}ms, first press in that column at {firstPressInCol.Time:F0}ms (delta: {firstPressInCol.Time - firstNote.Time:F0}ms)");
			}

			// Following Mania-Replay-Master's matching algorithm:
			// For each keypress, find the earliest note in that column that can be hit
			// - TOO_EARLY: press.time < note.time - missWindow
			// - TOO_LATE: press.time >= note.time + window100 (for regular notes)
			// - Notelock: if next note's time has arrived, current note is blocked

			// Track which notes have been hit
			var hitNotes = new HashSet<HitObject>();
			var noteDeviations = new Dictionary<HitObject, TimingDeviation>();

			// For LNs, track which press matched which LN so we can find the release
			var lnPressMatches = new Dictionary<HitObject, ManiaKeyEvent>();

			// Group notes by column for efficient lookup
			var notesByColumn = sortedHitObjects.GroupBy(n => n.Column)
				.ToDictionary(g => g.Key, g => g.ToList());

			// Track the index of the next unhit note in each column
			var nextNoteIndex = new Dictionary<int, int>();
			foreach (var col in notesByColumn.Keys) nextNoteIndex[col] = 0;

			// Process keypresses in chronological order (as osu! would)
			foreach (var press in pressEvents)
			{
				if (!notesByColumn.ContainsKey(press.Column))
					continue; // No notes in this column

				var columnNotes = notesByColumn[press.Column];
				var idx = nextNoteIndex[press.Column];

				// Skip notes that are no longer hittable
				while (idx < columnNotes.Count)
				{
					var currentNote = columnNotes[idx];
					var diff = press.Time - currentNote.Time;

					// For LNs, the late window extends to the LN end + 100 window
					// For regular notes, it's just note.Time + 100 window
					bool tooLate;
					if (currentNote.IsHold)
						// LN: can still hit until end + window100
						tooLate = press.Time > currentNote.EndTime + window100;
					else
						tooLate = diff >= window100;

					// Notelock: if next note's time has arrived, current note is blocked
					var blockedByNextNote = false;
					if (idx + 1 < columnNotes.Count)
					{
						var nextNote = columnNotes[idx + 1];
						blockedByNextNote = press.Time >= nextNote.Time;
					}

					if (tooLate || blockedByNextNote)
						idx++;
					else
						break;
				}

				nextNoteIndex[press.Column] = idx;

				if (idx >= columnNotes.Count)
					continue; // No more notes in this column (ghost tap)

				var targetNote = columnNotes[idx];
				var headDeviation = press.Time - targetNote.Time;

				// Check if the keypress is within the valid hit window
				var isTooEarly = -headDeviation > missWindow;
				bool isTooLate;
				if (targetNote.IsHold)
					isTooLate = press.Time > targetNote.EndTime + window100;
				else
					isTooLate = headDeviation >= window100;

				if (!isTooEarly && !isTooLate)
				{
					// Valid hit - consume the note
					hitNotes.Add(targetNote);
					nextNoteIndex[press.Column] = idx + 1;

					if (targetNote.IsHold)
					{
						// LN: Store the press for later release matching
						lnPressMatches[targetNote] = press;

						// Create a preliminary deviation (will be updated when we find the release)
						var timingDev = new TimingDeviation(
							targetNote.Time,
							press.Time,
							targetNote.Column,
							ManiaJudgement.Miss // Placeholder - will be calculated with release
						);
						timingDev.WasNeverHit = false;
						timingDev.IsHoldHead = true;
						noteDeviations[targetNote] = timingDev;
					}
					else
					{
						// Regular note: judge immediately
						var absDeviation = Math.Abs(headDeviation);
						var timingDev = new TimingDeviation(
							targetNote.Time,
							press.Time,
							targetNote.Column,
							TimingDeviation.GetJudgementFromDeviation(absDeviation, od)
						);
						timingDev.WasNeverHit = false;
						noteDeviations[targetNote] = timingDev;
					}
				}
				// else: TOO_EARLY = ghost tap (no note in window yet)
			}

			// Now match LN releases and calculate combined judgements
			var window50 = Get50Window(od);
			foreach (var (ln, press) in lnPressMatches)
			{
				// Find the release event in the same column AFTER the press
				var release = releaseEvents
					.Where(r => r.Column == ln.Column && r.Time > press.Time)
					.OrderBy(r => r.Time)
					.FirstOrDefault();

				var headDeviation = press.Time - ln.Time;
				double tailDeviation;

				if (release != null)
				{
					tailDeviation = release.Time - ln.EndTime;

					// Check if release is too early (released before LN end - 50 window)
					if (release.Time < ln.EndTime - window50)
						// Released too early - use a large penalty
						tailDeviation = ln.EndTime - release.Time; // Positive = how early
				}
				else
				{
					// No release found - held too long or to end
					tailDeviation = window100; // Penalty
				}

				// Calculate combined LN judgement using the centralized method
				var lnJudgement = TimingDeviation.GetLNJudgementFromDeviations(headDeviation, tailDeviation, od);

				// Update the deviation with correct judgement AND store tail deviation for recalculation
				if (noteDeviations.TryGetValue(ln, out var deviation))
				{
					var updatedDev = new TimingDeviation(
						deviation.ExpectedTime,
						deviation.ActualTime,
						deviation.Column,
						lnJudgement
					);
					updatedDev.WasNeverHit = false;
					updatedDev.IsHoldHead = true;
					updatedDev.TailDeviation = tailDeviation; // Store for accurate recalculation
					noteDeviations[ln] = updatedDev;
				}
			}

			// Debug: Show first 10 matched deviations
			var firstMatches = noteDeviations.Values.OrderBy(d => d.ExpectedTime).Take(10).ToList();
			Logger.Info(
				$"[DeviationCalc] First 10 matches (MAX=16ms, 300={64 - 3 * od:F0}ms, 200={97 - 3 * od:F0}ms, 100={127 - 3 * od:F0}ms):");
			foreach (var d in firstMatches)
				Logger.Info(
					$"[DeviationCalc]   Note@{d.ExpectedTime:F0}ms, Hit@{d.ActualTime:F0}ms, Dev={d.Deviation:F1}ms -> {d.Judgement}");

			// Build final deviation list in note order
			// Notes that weren't hit are misses
			foreach (var note in sortedHitObjects)
				if (noteDeviations.TryGetValue(note, out var deviation))
				{
					result.Deviations.Add(deviation);
				}
				else
				{
					// Note wasn't hit - it's a miss with no keypress
					var missDev = new TimingDeviation(
						note.Time,
						note.Time, // No actual hit time
						note.Column,
						ManiaJudgement.Miss
					);
					missDev.WasNeverHit = true; // This note had NO keypress - ALWAYS a Miss

					if (note.IsHold) missDev.IsHoldHead = true;

					result.Deviations.Add(missDev);
				}

			// Statistics
			var ghostTaps = pressEvents.Count - hitNotes.Count;
			var noteCount = sortedHitObjects.Count;
			var matchedCount = hitNotes.Count;
			var missCount = noteCount - matchedCount;
			var lnMatched = lnPressMatches.Count;
			var regularMatched = matchedCount - lnMatched;

			Logger.Info($"[DeviationCalc] Objects: {noteCount} ({regularNoteCount} notes, {lnCount} LNs)");
			Logger.Info(
				$"[DeviationCalc] Matched: {matchedCount} ({regularMatched} notes, {lnMatched} LNs), Missed: {missCount}, Ghost taps: {ghostTaps}");

			// Debug: Per-column breakdown
			foreach (var col in notesByColumn.Keys.OrderBy(c => c))
			{
				var colNotes = notesByColumn[col].Count;
				var colHit = notesByColumn[col].Count(n => hitNotes.Contains(n));
				var colPress = pressEvents.Count(p => p.Column == col);
				Logger.Info($"[DeviationCalc] Col{col}: {colNotes} notes, {colHit} hit, {colPress} presses");
			}

			// Debug: Show first 5 misses with analysis
			var firstMisses = sortedHitObjects.Where(n => !hitNotes.Contains(n)).Take(5);
			foreach (var miss in firstMisses)
			{
				var nearestPress = pressEvents.Where(p => p.Column == miss.Column)
					.OrderBy(p => Math.Abs(p.Time - miss.Time))
					.FirstOrDefault();
				if (nearestPress != null)
				{
					var gap = nearestPress.Time - miss.Time;
					var reason = "";
					if (-gap > missWindow)
						reason = "TOO_EARLY";
					else if (gap >= window100)
						reason = "TOO_LATE";
					else
						reason = "notelock?";
					Logger.Info(
						$"[DeviationCalc] MISS: {miss.Time:F0}ms Col{miss.Column}, nearest@{nearestPress.Time:F0}ms (gap={gap:F0}ms, {reason})");
				}
				else
				{
					Logger.Info($"[DeviationCalc] MISS: {miss.Time:F0}ms Col{miss.Column}, no presses in column!");
				}
			}

			// Calculate statistics
			result.CalculateStatistics();
			result.OverallDifficulty = od;
			result.Success = true;

			Logger.Info(
				$"[DeviationCalc] Analysis complete: {result.Deviations.Count} deviations, UR={result.UnstableRate:F2}, Mean={result.MeanDeviation:F2}ms");
		}
		catch (Exception ex)
		{
			Logger.Info($"[DeviationCalc] Error calculating deviations: {ex.Message}");
			result.Success = false;
			result.ErrorMessage = ex.Message;
		}

		return result;
	}

	/// <summary>
	/// Parses hit objects from the osu file's raw HitObjects section.
	/// </summary>
	[SuppressMessage("", "CA1310")]
	private static List<HitObject> ParseHitObjects(OsuFile osuFile, int keyCount)
	{
		var hitObjects = new List<HitObject>();

		if (!osuFile.RawSections.TryGetValue("HitObjects", out var hitObjectLines)) return hitObjects;

		foreach (var line in hitObjectLines)
		{
			if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
				continue;

			var hitObject = HitObject.Parse(line, keyCount);
			if (hitObject != null) hitObjects.Add(hitObject);
		}

		return hitObjects;
	}
}
