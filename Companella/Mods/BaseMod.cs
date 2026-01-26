using Companella.Models.Beatmap;
using Companella.Mods.Parameters;
using Companella.Services.Common;

namespace Companella.Mods;

/// <summary>
/// Abstract base class for all beatmap mods.
/// Provides common functionality like validation, error handling, and logging.
/// </summary>
public abstract class BaseMod : IMod
{
	private readonly List<IModParameter> _parameters = new();

	/// <inheritdoc />
	public abstract string Name { get; }

	/// <inheritdoc />
	public abstract string Description { get; }

	/// <inheritdoc />
	public virtual string Category => "General";

	/// <inheritdoc />
	public abstract string Icon { get; }

	/// <inheritdoc />
	public IReadOnlyList<IModParameter> Parameters => _parameters;

	/// <summary>
	/// Registers a parameter with this mod.
	/// Call this in the constructor of derived classes.
	/// </summary>
	/// <param name="parameter">The parameter to register.</param>
	protected void AddParameter(IModParameter parameter)
	{
		_parameters.Add(parameter);
	}

	/// <summary>
	/// Resets all parameters to their default values.
	/// </summary>
	public void ResetParameters()
	{
		foreach (var param in _parameters) param.Reset();
	}

	/// <summary>
	/// Applies the mod to the given context with validation and error handling.
	/// </summary>
	/// <param name="context">The mod context containing source data.</param>
	/// <returns>The result of applying the mod.</returns>
	public ModResult Apply(ModContext context)
	{
		try
		{
			// Validate context
			var validationError = ValidateContext(context);
			if (validationError != null)
			{
				Logger.Info($"[{Name}] Validation failed: {validationError}");
				return ModResult.Failed(validationError);
			}

			Logger.Info($"[{Name}] Applying mod to {context.HitObjects.Count} hit objects...");

			// Apply the mod
			var result = ApplyInternal(context);

			if (result.Success)
				Logger.Info($"[{Name}] Mod applied successfully. {result.Statistics?.ToString() ?? ""}");
			else
				Logger.Info($"[{Name}] Mod failed: {result.ErrorMessage}");

			return result;
		}
		catch (Exception ex)
		{
			Logger.Info($"[{Name}] Exception during mod application: {ex.Message}");
			return ModResult.Failed($"Unexpected error: {ex.Message}");
		}
	}

	/// <summary>
	/// Validates the context before applying the mod.
	/// Override this to add custom validation.
	/// </summary>
	/// <param name="context">The context to validate.</param>
	/// <returns>An error message if validation fails, null if valid.</returns>
	protected virtual string? ValidateContext(ModContext context)
	{
		if (context == null)
			return "Context is null";

		if (context.SourceFile == null)
			return "Source file is null";

		if (context.HitObjects == null)
			return "Hit objects list is null";

		if (context.HitObjects.Count == 0)
			return "No hit objects to modify";

		if (context.KeyCount < 1 || context.KeyCount > 18)
			return $"Invalid key count: {context.KeyCount}";

		// Check if this is a mania beatmap
		if (context.SourceFile.Mode != 3)
			return $"Not a mania beatmap (mode {context.SourceFile.Mode})";

		return null;
	}

	/// <summary>
	/// Internal method that applies the mod logic.
	/// Subclasses must implement this method.
	/// </summary>
	/// <param name="context">The validated mod context.</param>
	/// <returns>The result of applying the mod.</returns>
	protected abstract ModResult ApplyInternal(ModContext context);

	/// <summary>
	/// Helper method to create a deep copy of hit objects for modification.
	/// </summary>
	/// <param name="hitObjects">The hit objects to clone.</param>
	/// <returns>A new list with cloned hit objects.</returns>
	protected static List<HitObject> CloneHitObjects(List<HitObject> hitObjects)
	{
		return hitObjects.Select(ho => ho.Clone()).ToList();
	}

	/// <summary>
	/// Helper method to calculate statistics from before and after hit objects.
	/// </summary>
	/// <param name="original">The original hit objects.</param>
	/// <param name="modified">The modified hit objects.</param>
	/// <returns>Statistics about the modification.</returns>
	protected static ModStatistics CalculateStatistics(List<HitObject> original, List<HitObject> modified)
	{
		var stats = new ModStatistics
		{
			OriginalNoteCount = original.Count,
			ModifiedNoteCount = modified.Count
		};

		var originalHolds = original.Count(ho => ho.IsHold);
		var modifiedHolds = modified.Count(ho => ho.IsHold);

		if (modifiedHolds > originalHolds)
			stats.CirclesToHolds = modifiedHolds - originalHolds;
		else if (modifiedHolds < originalHolds) stats.HoldsToCircles = originalHolds - modifiedHolds;

		stats.NotesChanged = stats.CirclesToHolds + stats.HoldsToCircles;

		return stats;
	}
}
