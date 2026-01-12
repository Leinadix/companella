using Companella.Mods.Parameters;

namespace Companella.Mods;

/// <summary>
/// Interface defining the contract for beatmap modification tools (mods).
/// Mods transform hit objects in a beatmap and output to a new file.
/// </summary>
public interface IMod
{
    /// <summary>
    /// The display name of the mod.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// A brief description of what the mod does.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// The category this mod belongs to (e.g., "LN Tools", "Pattern Tools").
    /// </summary>
    string Category { get; }

    /// <summary>
    /// The Shorthand name abbreviation of the mod.
    /// </summary>
    string Icon { get; }

    /// <summary>
    /// The configurable parameters exposed by this mod.
    /// </summary>
    IReadOnlyList<IModParameter> Parameters { get; }

    /// <summary>
    /// Applies the mod to the given context and returns the result.
    /// </summary>
    /// <param name="context">The mod context containing source data and hit objects.</param>
    /// <returns>The result of applying the mod, including modified hit objects.</returns>
    ModResult Apply(ModContext context);
}
