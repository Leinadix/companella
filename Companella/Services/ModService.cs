using System.Text.RegularExpressions;
using Companella.Models.Beatmap;
using Companella.Mods;

namespace Companella.Services;

/// <summary>
/// Service for registering, managing, and executing beatmap mods.
/// Acts as the central point of access for the mod system.
/// </summary>
public class ModService
{
    private readonly Dictionary<string, IMod> _registeredMods = new(StringComparer.OrdinalIgnoreCase);
    private readonly HitObjectSerializer _hitObjectSerializer;

    /// <summary>
    /// Creates a new ModService instance.
    /// </summary>
    public ModService()
    {
        _hitObjectSerializer = new HitObjectSerializer();
    }

    /// <summary>
    /// Creates a new ModService instance with a custom serializer.
    /// </summary>
    /// <param name="hitObjectSerializer">The hit object serializer to use.</param>
    public ModService(HitObjectSerializer hitObjectSerializer)
    {
        _hitObjectSerializer = hitObjectSerializer ?? throw new ArgumentNullException(nameof(hitObjectSerializer));
    }

    /// <summary>
    /// Registers a mod with the service.
    /// </summary>
    /// <param name="mod">The mod to register.</param>
    /// <exception cref="ArgumentException">Thrown if a mod with the same name is already registered.</exception>
    public void RegisterMod(IMod mod)
    {
        if (mod == null)
            throw new ArgumentNullException(nameof(mod));

        if (_registeredMods.ContainsKey(mod.Name))
            throw new ArgumentException($"A mod with name '{mod.Name}' is already registered");

        _registeredMods[mod.Name] = mod;
        Logger.Info($"[ModService] Registered mod: {mod.Name} ({mod.Category})");
    }

    /// <summary>
    /// Unregisters a mod from the service.
    /// </summary>
    /// <param name="modName">The name of the mod to unregister.</param>
    /// <returns>True if the mod was unregistered, false if it wasn't found.</returns>
    public bool UnregisterMod(string modName)
    {
        if (string.IsNullOrEmpty(modName))
            return false;

        var removed = _registeredMods.Remove(modName);
        if (removed)
        {
            Logger.Info($"[ModService] Unregistered mod: {modName}");
        }
        return removed;
    }

    /// <summary>
    /// Gets a registered mod by name.
    /// </summary>
    /// <param name="modName">The name of the mod.</param>
    /// <returns>The mod if found, null otherwise.</returns>
    public IMod? GetMod(string modName)
    {
        if (string.IsNullOrEmpty(modName))
            return null;

        _registeredMods.TryGetValue(modName, out var mod);
        return mod;
    }

    /// <summary>
    /// Gets all registered mods.
    /// </summary>
    /// <returns>A list of all registered mods.</returns>
    public IReadOnlyList<IMod> GetAllMods()
    {
        return _registeredMods.Values.ToList();
    }

    /// <summary>
    /// Gets all registered mods in a specific category.
    /// </summary>
    /// <param name="category">The category to filter by.</param>
    /// <returns>A list of mods in the specified category.</returns>
    public IReadOnlyList<IMod> GetModsByCategory(string category)
    {
        return _registeredMods.Values
            .Where(m => string.Equals(m.Category, category, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Gets all unique categories of registered mods.
    /// </summary>
    /// <returns>A list of category names.</returns>
    public IReadOnlyList<string> GetCategories()
    {
        return _registeredMods.Values
            .Select(m => m.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c)
            .ToList();
    }

    /// <summary>
    /// Applies a mod to an osu file and writes the result to a new file.
    /// </summary>
    /// <param name="mod">The mod to apply.</param>
    /// <param name="osuFile">The source osu file.</param>
    /// <param name="outputPath">The path for the output file (optional, auto-generated if null).</param>
    /// <param name="progressCallback">Optional callback for progress updates.</param>
    /// <returns>The result of applying the mod.</returns>
    public async Task<ModResult> ApplyModAsync(
        IMod mod,
        OsuFile osuFile,
        string? outputPath = null,
        Action<string>? progressCallback = null)
    {
        if (mod == null)
            throw new ArgumentNullException(nameof(mod));
        if (osuFile == null)
            throw new ArgumentNullException(nameof(osuFile));

        try
        {
            progressCallback?.Invoke($"Parsing hit objects...");

            // Parse hit objects
            var hitObjects = await Task.Run(() => _hitObjectSerializer.Parse(osuFile));

            progressCallback?.Invoke($"Applying {mod.Name}...");

            // Create context
            var context = new ModContext(osuFile, hitObjects);

            // Apply mod
            var result = await Task.Run(() => mod.Apply(context));

            if (!result.Success)
            {
                return result;
            }

            progressCallback?.Invoke("Writing output file...");

            // Generate output path if not provided
            outputPath ??= GenerateOutputPath(osuFile, mod);

            // Write the modified beatmap
            await WriteModifiedBeatmapAsync(osuFile, result.ModifiedHitObjects!, outputPath, context.KeyCount, mod);

            result.OutputFilePath = outputPath;

            progressCallback?.Invoke($"Mod applied successfully!");
            Logger.Info($"[ModService] Mod '{mod.Name}' applied. Output: {outputPath}");

            return result;
        }
        catch (Exception ex)
        {
            Logger.Info($"[ModService] Error applying mod '{mod.Name}': {ex.Message}");
            return ModResult.Failed($"Error applying mod: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies a mod by name to an osu file.
    /// </summary>
    /// <param name="modName">The name of the mod to apply.</param>
    /// <param name="osuFile">The source osu file.</param>
    /// <param name="outputPath">The path for the output file (optional).</param>
    /// <param name="progressCallback">Optional callback for progress updates.</param>
    /// <returns>The result of applying the mod.</returns>
    public async Task<ModResult> ApplyModByNameAsync(
        string modName,
        OsuFile osuFile,
        string? outputPath = null,
        Action<string>? progressCallback = null)
    {
        var mod = GetMod(modName);
        if (mod == null)
        {
            return ModResult.Failed($"Mod '{modName}' not found");
        }

        return await ApplyModAsync(mod, osuFile, outputPath, progressCallback);
    }

    /// <summary>
    /// Generates an output path for a modded beatmap.
    /// </summary>
    /// <param name="osuFile">The source osu file.</param>
    /// <param name="mod">The mod being applied.</param>
    /// <returns>The generated output path.</returns>
    private string GenerateOutputPath(OsuFile osuFile, IMod mod)
    {
        var directory = osuFile.DirectoryPath;
        var originalBaseName = Path.GetFileNameWithoutExtension(osuFile.FilePath);
        var modSuffix = $"[+{mod.Icon}]";

        // Try to extract the part before the difficulty name in brackets
        var match = Regex.Match(originalBaseName, @"^(.+?)\s*\[(.+)\]$");
        string newBaseName;

        if (match.Success)
        {
            var prefix = match.Groups[1].Value;
            var originalDiff = match.Groups[2].Value;
            newBaseName = $"{prefix} [{originalDiff} {modSuffix}]";
        }
        else
        {
            newBaseName = $"{originalBaseName} {modSuffix}";
        }

        // Sanitize filename
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitizedBaseName = string.Join("_", newBaseName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

        return Path.Combine(directory, sanitizedBaseName + ".osu");
    }

    /// <summary>
    /// Writes a modified beatmap to a new file.
    /// </summary>
    private async Task WriteModifiedBeatmapAsync(
        OsuFile originalFile,
        List<HitObject> modifiedHitObjects,
        string outputPath,
        int keyCount,
        IMod mod)
    {
        // Read original file
        var lines = await File.ReadAllLinesAsync(originalFile.FilePath);
        var result = new List<string>();
        var modSuffix = $"[+{mod.Icon}]";

        bool inHitObjectsSection = false;
        bool hitObjectsWritten = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Check for section header
            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
            {
                // If we were in HitObjects section, write modified hit objects before leaving
                if (inHitObjectsSection && !hitObjectsWritten)
                {
                    WriteHitObjects(result, modifiedHitObjects, keyCount);
                    hitObjectsWritten = true;
                }

                inHitObjectsSection = trimmed == "[HitObjects]";
                result.Add(line);
                continue;
            }

            // If in HitObjects section, skip original lines (we'll write new ones)
            if (inHitObjectsSection)
            {
                // Skip original hit object data, but keep comments
                if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("//"))
                    continue;
            }

            // Modify metadata section to update Version and clear BeatmapID
            if (trimmed.StartsWith("Version:"))
            {
                var originalVersion = trimmed.Substring("Version:".Length).Trim();
                result.Add($"Version:{originalVersion} {modSuffix}");
                continue;
            }

            if (trimmed.StartsWith("BeatmapID:"))
            {
                result.Add("BeatmapID:0");
                continue;
            }

            result.Add(line);
        }

        // If file ends while still in HitObjects section
        if (inHitObjectsSection && !hitObjectsWritten)
        {
            WriteHitObjects(result, modifiedHitObjects, keyCount);
        }

        // Write output file
        await File.WriteAllLinesAsync(outputPath, result);
    }

    /// <summary>
    /// Writes hit objects to the result list.
    /// </summary>
    private void WriteHitObjects(List<string> result, List<HitObject> hitObjects, int keyCount)
    {
        var serialized = _hitObjectSerializer.SerializeAll(hitObjects, keyCount);
        foreach (var line in serialized)
        {
            result.Add(line);
        }
    }

    /// <summary>
    /// Creates a ModContext for an OsuFile without applying a mod.
    /// Useful for testing or previewing.
    /// </summary>
    /// <param name="osuFile">The osu file to create context for.</param>
    /// <returns>A mod context with parsed hit objects.</returns>
    public ModContext CreateContext(OsuFile osuFile)
    {
        var hitObjects = _hitObjectSerializer.Parse(osuFile);
        return new ModContext(osuFile, hitObjects);
    }
}
