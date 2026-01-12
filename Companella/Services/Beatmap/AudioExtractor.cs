using Companella.Models.Beatmap;

namespace Companella.Services.Beatmap;

/// <summary>
/// Handles locating audio files from osu! beatmaps.
/// </summary>
public class AudioExtractor
{
    /// <summary>
    /// Gets the full path to the audio file for a beatmap.
    /// </summary>
    public string GetAudioPath(OsuFile osuFile)
    {
        if (string.IsNullOrEmpty(osuFile.AudioFilename))
            throw new InvalidOperationException("Beatmap has no audio file specified.");

        var audioPath = osuFile.AudioFilePath;

        if (!File.Exists(audioPath))
            throw new FileNotFoundException($"Audio file not found: {audioPath}");

        return audioPath;
    }

    /// <summary>
    /// Validates that the audio file exists and is accessible.
    /// </summary>
    public bool ValidateAudioFile(OsuFile osuFile)
    {
        if (string.IsNullOrEmpty(osuFile.AudioFilename))
            return false;

        return File.Exists(osuFile.AudioFilePath);
    }

    /// <summary>
    /// Gets the audio file extension (e.g., ".mp3", ".ogg").
    /// </summary>
    public string GetAudioExtension(OsuFile osuFile)
    {
        return Path.GetExtension(osuFile.AudioFilename).ToLowerInvariant();
    }
}
