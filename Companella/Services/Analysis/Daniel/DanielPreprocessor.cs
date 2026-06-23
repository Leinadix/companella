// Daniel rice difficulty algorithm — Copyright (c) 2026 TheBagelOfMan
// Ported from https://github.com/TheBagelOfMan/Daniel (MIT License; see LICENSE in this folder)

using Companella.Models.Beatmap;

namespace Companella.Services.Analysis.Daniel;

/// <summary>
/// A note head in Daniel's internal representation (column, time ms).
/// </summary>
internal readonly record struct DanielNote(int Column, int Time);

/// <summary>
/// Preprocessed beatmap data for Daniel difficulty calculation.
/// </summary>
internal sealed class DanielPreprocessResult
{
	public required double X { get; init; }
	public required int KeyCount { get; init; }
	public required int T { get; init; }
	public required List<DanielNote> NoteSeq { get; init; }
	public required List<List<DanielNote>> NoteSeqByColumn { get; init; }
}

/// <summary>
/// Preprocesses hit objects into Daniel's note sequence format.
/// </summary>
internal static class DanielPreprocessor
{
	internal static DanielPreprocessResult Preprocess(IReadOnlyList<DanielNote> noteSeq, int keyCount)
	{
		var noteDict = new Dictionary<int, List<DanielNote>>();
		foreach (var note in noteSeq)
		{
			if (!noteDict.TryGetValue(note.Column, out var list))
			{
				list = [];
				noteDict[note.Column] = list;
			}

			list.Add(note);
		}

		var noteSeqByColumn = noteDict.Values
			.OrderBy(notes => notes[0].Column)
			.ToList();

		var x = 0.3 * Math.Pow((64.5 - Math.Ceiling(DanielConstants.HardcodedOd * 3)) / 500.0, 0.5);
		x = Math.Min(x, 0.6 * (x - 0.09) + 0.09);

		var t = noteSeq.Count > 0 ? noteSeq.Max(n => n.Time) + 1 : 1;

		return new DanielPreprocessResult
		{
			X = x,
			KeyCount = keyCount,
			T = t,
			NoteSeq = noteSeq.ToList(),
			NoteSeqByColumn = noteSeqByColumn
		};
	}

	internal static DanielPreprocessResult Preprocess(IReadOnlyList<HitObject> hitObjects, int keyCount, float rate)
	{
		var noteSeq = new List<DanielNote>(hitObjects.Count);

		foreach (var hit in hitObjects)
		{
			var time = ApplyRate(hit.Time, rate);
			noteSeq.Add(new DanielNote(hit.Column, time));
		}

		noteSeq.Sort((a, b) =>
		{
			var timeCompare = a.Time.CompareTo(b.Time);
			return timeCompare != 0 ? timeCompare : a.Column.CompareTo(b.Column);
		});

		return Preprocess(noteSeq, keyCount);
	}

	private static int ApplyRate(double timeMs, float rate)
	{
		if (Math.Abs(rate - 1.5f) < 0.01f)
			return (int)Math.Floor(timeMs * 2.0 / 3.0);

		if (Math.Abs(rate - 0.75f) < 0.01f)
			return (int)Math.Floor(timeMs * 4.0 / 3.0);

		if (Math.Abs(rate - 1.0f) < 0.0001f)
			return (int)Math.Floor(timeMs);

		return (int)Math.Floor(timeMs / rate);
	}
}
