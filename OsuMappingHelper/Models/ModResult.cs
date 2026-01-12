namespace OsuMappingHelper.Models;

/// <summary>
/// Result of applying a mod to a beatmap.
/// </summary>
public class ModResult
{
    /// <summary>
    /// Whether the mod was applied successfully.
    /// </summary>
    public bool Success { get; private set; }

    /// <summary>
    /// Error message if the mod failed (null if successful).
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// The modified hit objects (null if failed).
    /// </summary>
    public List<HitObject>? ModifiedHitObjects { get; private set; }

    /// <summary>
    /// The path to the output file (set by ModService after writing).
    /// </summary>
    public string? OutputFilePath { get; set; }

    /// <summary>
    /// Statistics about the modification (optional).
    /// </summary>
    public ModStatistics? Statistics { get; set; }

    /// <summary>
    /// Private constructor - use static factory methods.
    /// </summary>
    private ModResult() { }

    /// <summary>
    /// Creates a successful result with modified hit objects.
    /// </summary>
    /// <param name="modifiedHitObjects">The modified hit objects.</param>
    /// <param name="statistics">Optional statistics about the modification.</param>
    public static ModResult Succeeded(List<HitObject> modifiedHitObjects, ModStatistics? statistics = null)
    {
        return new ModResult
        {
            Success = true,
            ModifiedHitObjects = modifiedHitObjects ?? throw new ArgumentNullException(nameof(modifiedHitObjects)),
            Statistics = statistics
        };
    }

    /// <summary>
    /// Creates a failed result with an error message.
    /// </summary>
    /// <param name="errorMessage">The error message describing the failure.</param>
    public static ModResult Failed(string errorMessage)
    {
        return new ModResult
        {
            Success = false,
            ErrorMessage = errorMessage ?? "Unknown error"
        };
    }
}

/// <summary>
/// Statistics about a mod application.
/// </summary>
public class ModStatistics
{
    /// <summary>
    /// Total number of notes in the original beatmap.
    /// </summary>
    public int OriginalNoteCount { get; set; }

    /// <summary>
    /// Total number of notes after modification.
    /// </summary>
    public int ModifiedNoteCount { get; set; }

    /// <summary>
    /// Number of notes that were changed.
    /// </summary>
    public int NotesChanged { get; set; }

    /// <summary>
    /// Number of circles converted to holds.
    /// </summary>
    public int CirclesToHolds { get; set; }

    /// <summary>
    /// Number of holds converted to circles.
    /// </summary>
    public int HoldsToCircles { get; set; }

    /// <summary>
    /// Additional custom statistics as key-value pairs.
    /// </summary>
    public Dictionary<string, object> CustomStats { get; set; } = new();

    /// <summary>
    /// Returns a summary string of the statistics.
    /// </summary>
    public override string ToString()
    {
        var parts = new List<string>();

        if (NotesChanged > 0)
            parts.Add($"{NotesChanged} notes changed");
        if (CirclesToHolds > 0)
            parts.Add($"{CirclesToHolds} circles -> holds");
        if (HoldsToCircles > 0)
            parts.Add($"{HoldsToCircles} holds -> circles");

        foreach (var kvp in CustomStats)
        {
            parts.Add($"{kvp.Key}: {kvp.Value}");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "No changes";
    }
}
