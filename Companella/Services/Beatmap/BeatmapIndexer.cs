using System.Text;
using System.Text.RegularExpressions;
using Companella.Services.Common;

namespace Companella.Services.Beatmap;

/// <summary>
/// Service for creating indexed copies of beatmap files.
/// Copies .osu files and modifies the Version field to add an index prefix.
/// </summary>
public class BeatmapIndexer
{
    /// <summary>
    /// Tag added to session copies to identify them as Companella-generated files.
    /// </summary>
    public const string SessionTag = "companellasessiondonottouch";

    /// <summary>
    /// Creates an indexed copy of a beatmap file.
    /// The copy will have the Version field modified to include an index prefix,
    /// and the Tags field will include the session marker tag.
    /// </summary>
    /// <param name="originalPath">Path to the original .osu file.</param>
    /// <param name="index">The index to prefix (will be formatted as #001, #002, etc.).</param>
    /// <returns>Path to the newly created indexed copy, or null if failed.</returns>
    public string? CreateIndexedCopy(string originalPath, int index)
    {
        if (!File.Exists(originalPath))
        {
            Logger.Info($"[BeatmapIndexer] Original file not found: {originalPath}");
            return null;
        }

        try
        {
            var directory = Path.GetDirectoryName(originalPath);
            if (string.IsNullOrEmpty(directory))
            {
                Logger.Info("[BeatmapIndexer] Could not determine directory");
                return null;
            }

            // Read the original file
            var lines = File.ReadAllLines(originalPath, Encoding.UTF8);
            var modifiedLines = new List<string>();
            string? originalVersion = null;
            string? newVersion = null;
            bool foundTags = false;

            // Process each line
            bool inMetadataSection = false;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                // Track section
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    // If leaving Metadata section and haven't found Tags, add it
                    if (inMetadataSection && !foundTags)
                    {
                        modifiedLines.Add($"Tags:{SessionTag}");
                        foundTags = true;
                    }
                    inMetadataSection = trimmed == "[Metadata]";
                    modifiedLines.Add(line);
                    continue;
                }

                // Modify Version field in Metadata section
                if (inMetadataSection && trimmed.StartsWith("Version:"))
                {
                    originalVersion = trimmed.Substring("Version:".Length).Trim();
                    newVersion = $"#{index:D3} {originalVersion}";
                    modifiedLines.Add($"Version:{newVersion}");
                    continue;
                }

                // Add session tag to Tags field in Metadata section
                if (inMetadataSection && trimmed.StartsWith("Tags:"))
                {
                    var existingTags = trimmed.Substring("Tags:".Length).Trim();
                    // Only add tag if not already present
                    if (!existingTags.Contains(SessionTag))
                    {
                        if (string.IsNullOrWhiteSpace(existingTags))
                        {
                            modifiedLines.Add($"Tags:{SessionTag}");
                        }
                        else
                        {
                            modifiedLines.Add($"Tags:{existingTags} {SessionTag}");
                        }
                    }
                    else
                    {
                        modifiedLines.Add(line);
                    }
                    foundTags = true;
                    continue;
                }

                modifiedLines.Add(line);
            }

            if (string.IsNullOrEmpty(originalVersion) || string.IsNullOrEmpty(newVersion))
            {
                Logger.Info("[BeatmapIndexer] Could not find Version field in file");
                return null;
            }

            // Generate new filename
            // Original: "Artist - Title (Creator) [Diff].osu"
            // New: "Artist - Title (Creator) [#001 Diff].osu"
            var originalFilename = Path.GetFileName(originalPath);
            var newFilename = GenerateIndexedFilename(originalFilename, index, originalVersion, newVersion);
            var newPath = Path.Combine(directory, newFilename);

            // Write the modified file
            File.WriteAllLines(newPath, modifiedLines, Encoding.UTF8);

            Logger.Info($"[BeatmapIndexer] Created indexed copy: {newFilename}");
            return newPath;
        }
        catch (Exception ex)
        {
            Logger.Info($"[BeatmapIndexer] Error creating indexed copy: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Generates a new filename with the indexed version name.
    /// </summary>
    private string GenerateIndexedFilename(string originalFilename, int index, string originalVersion, string newVersion)
    {
        // Try to replace the version in the filename
        // Typical format: "Artist - Title (Creator) [Diff].osu"
        var pattern = Regex.Escape($"[{originalVersion}]");
        var replacement = $"[{newVersion}]";

        var newFilename = Regex.Replace(originalFilename, pattern, replacement);

        // If the pattern wasn't found, just prepend the index to the filename
        if (newFilename == originalFilename)
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(originalFilename);
            newFilename = $"{nameWithoutExt}_#{index:D3}.osu";
        }

        // Sanitize the filename (remove invalid characters)
        newFilename = SanitizeFilename(newFilename);

        return newFilename;
    }

    /// <summary>
    /// Removes invalid filename characters.
    /// </summary>
    private string SanitizeFilename(string filename)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new StringBuilder();

        foreach (var c in filename)
        {
            if (!invalidChars.Contains(c))
            {
                sanitized.Append(c);
            }
        }

        return sanitized.ToString();
    }

    /// <summary>
    /// Cleans up all indexed copies in a directory that match the session pattern.
    /// </summary>
    /// <param name="directory">Directory to clean.</param>
    /// <param name="pattern">Pattern to match (e.g., "#001", "#002").</param>
    public void CleanupIndexedCopies(string directory, string? pattern = null)
    {
        if (!Directory.Exists(directory))
            return;

        try
        {
            var files = Directory.GetFiles(directory, "*.osu");
            var indexPattern = pattern ?? @"\[#\d{3}";

            foreach (var file in files)
            {
                var filename = Path.GetFileName(file);
                if (Regex.IsMatch(filename, indexPattern))
                {
                    try
                    {
                        File.Delete(file);
                        Logger.Info($"[BeatmapIndexer] Deleted indexed copy: {filename}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Info($"[BeatmapIndexer] Failed to delete {filename}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Info($"[BeatmapIndexer] Error cleaning up indexed copies: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates multiple indexed copies for a list of beatmap paths.
    /// </summary>
    /// <param name="beatmapPaths">List of original beatmap paths.</param>
    /// <returns>List of paths to the created indexed copies.</returns>
    public List<string> CreateIndexedCopies(IEnumerable<string> beatmapPaths)
    {
        var indexedPaths = new List<string>();
        var index = 1;

        foreach (var path in beatmapPaths)
        {
            var indexedPath = CreateIndexedCopy(path, index);
            if (!string.IsNullOrEmpty(indexedPath))
            {
                indexedPaths.Add(indexedPath);
                index++;
            }
        }

        return indexedPaths;
    }
}

