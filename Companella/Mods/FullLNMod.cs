using Companella.Models.Beatmap;
using Companella.Mods.Parameters;

namespace Companella.Mods;

public class FullLNMod : BaseMod
{
	private readonly ModParameter<int> _snapDivisor;

	public override string Name => "Full LN";
	public override string Description => "Converts all circles to long notes";
	public override string Category => "General";
	public override string Icon => "FLN";

	public FullLNMod()
	{
		_snapDivisor = new ModParameter<int>(
			"Snap Divisor",
			"LN ends 1/N beat before the next note (e.g., 4 = 1/4 beat gap)",
			4,
			1,
			16,
			1);
		AddParameter(_snapDivisor);
	}

	protected override ModResult ApplyInternal(ModContext context)
	{
		var snapDivisor = _snapDivisor.Value;

		// Group hit objects by column and sort by time
		var byColumn = context.HitObjects
			.Select(ho => ho.Clone())
			.GroupBy(ho => ho.Column)
			.ToDictionary(g => g.Key, g => g.OrderBy(ho => ho.Time).ToList());

		var modified = new List<HitObject>();

		foreach (var (column, notes) in byColumn)
			for (var i = 0; i < notes.Count; i++)
			{
				var note = notes[i];

				// Skip if already a hold note
				if (note.IsHold)
				{
					modified.Add(note);
					continue;
				}

				// Convert circle to hold
				note.Type = HitObjectType.Hold;

				// Calculate end time: 1/N beat before the next note, or 1/N beat duration if no next note
				var snapBeat = context.GetSnapDuration(note.Time, snapDivisor);

				if (i + 1 < notes.Count)
				{
					var nextNote = notes[i + 1];
					var nextNoteStart = nextNote.Time;

					// End 1/N beat before the next note
					var desiredEndTime = nextNoteStart - snapBeat;

					// Notes must always be at least 1 ms long
					if (desiredEndTime <= note.Time) desiredEndTime = note.Time + 1;

					note.EndTime = desiredEndTime;
				}
				else
				{
					// No next note in this column - make it a 1/N beat duration LN
					note.EndTime = note.Time + snapBeat;
				}

				modified.Add(note);
			}

		// Sort by time for consistent output
		modified = modified.OrderBy(ho => ho.Time).ThenBy(ho => ho.Column).ToList();

		var stats = CalculateStatistics(context.HitObjects, modified);
		return ModResult.Succeeded(modified, stats);
	}
}
