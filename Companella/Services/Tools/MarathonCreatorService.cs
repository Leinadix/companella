using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Companella.Models.Application;
using Companella.Models.Beatmap;
using Companella.Models.Difficulty;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Companella.Services.Beatmap;
using Companella.Services.Common;
// Aliases to avoid conflicts with SixLabors.ImageSharp types
using IOPath = System.IO.Path;

namespace Companella.Services.Tools;

/// <summary>
/// Metadata for the created marathon beatmap.
/// </summary>
public class MarathonMetadata
{
    public string Title { get; set; } = "Marathon";
    public string TitleUnicode { get; set; } = "";
    public string Artist { get; set; } = "Various Artists";
    public string ArtistUnicode { get; set; } = "";
    public string Creator { get; set; } = "Companella";
    public string Version { get; set; } = "Marathon";
    public string Tags { get; set; } = "marathon companella";
    
    /// <summary>
    /// Text to display in the center circle of the background (up to 3 characters).
    /// Can be unicode letters or symbols.
    /// </summary>
    public string CenterText { get; set; } = "";
    
    /// <summary>
    /// Intensity of glitch effects on the background (0.0 = none, 1.0 = maximum).
    /// Effects include RGB shift, scanlines, distortion, and block glitches.
    /// </summary>
    public float GlitchIntensity { get; set; } = 0f;
}

/// <summary>
/// Result of marathon creation.
/// </summary>
public class MarathonCreationResult
{
    public bool Success { get; set; }
    public string? OutputPath { get; set; }
    public string? ErrorMessage { get; set; }
    public double TotalDurationMs { get; set; }
}

/// <summary>
/// Information about highlight images generated for storyboard.
/// </summary>
internal class HighlightInfo
{
    /// <summary>
    /// Maps MarathonEntry ID to the highlight image filename.
    /// </summary>
    public Dictionary<Guid, string> EntryToHighlightFile { get; set; } = new();
}

/// <summary>
/// Pre-calculated fade information for an entry.
/// </summary>
internal class EntryFadeInfo
{
    public int Index { get; set; }
    public bool IsPause { get; set; }
    
    /// <summary>For maps: how much EXTRA intro audio to include before first note (ms).</summary>
    public double IntroExtensionMs { get; set; }
    
    /// <summary>For maps: how much EXTRA outro audio to include after last note (ms).</summary>
    public double OutroExtensionMs { get; set; }
    
    /// <summary>For maps: fade-in duration (equals extension - we only fade audio before notes, never during).</summary>
    public double IntroFadeDurationMs { get; set; }
    
    /// <summary>For maps: fade-out duration (equals extension - we only fade audio after notes, never during).</summary>
    public double OutroFadeDurationMs { get; set; }
    
    /// <summary>For pauses: remaining duration after adjacent fades consume it (ms).</summary>
    public double RemainingPauseDurationMs { get; set; }
}

/// <summary>
/// Internal data for a processed map segment.
/// </summary>
internal class ProcessedMapSegment
{
    public MarathonEntry Entry { get; set; } = null!;
    public string TrimmedAudioPath { get; set; } = string.Empty;
    public string ProcessedOsuPath { get; set; } = string.Empty;
    public double OffsetInMarathon { get; set; }
    public double Duration { get; set; }
    /// <summary>
    /// How much audio was included before the first note (for fade-in during preceding pause).
    /// </summary>
    public double IntroFadeDuration { get; set; }
    /// <summary>
    /// How much audio was included after the last note (for fade-out during following pause).
    /// </summary>
    public double OutroFadeDuration { get; set; }
    public List<string> ShiftedHitObjectLines { get; set; } = new();
    public List<string> ShiftedTimingPointLines { get; set; } = new();
    public List<string> ShiftedEventLines { get; set; } = new();
}

/// <summary>
/// Service for creating marathon beatmaps by merging multiple maps together.
/// </summary>
public class MarathonCreatorService
{
    private readonly string _ffmpegPath;
    private readonly OsuFileParser _fileParser;
    private readonly RateChanger _rateChanger;

    public MarathonCreatorService(string ffmpegPath = "ffmpeg")
    {
        _ffmpegPath = ffmpegPath;
        _fileParser = new OsuFileParser();
        _rateChanger = new RateChanger(ffmpegPath);
    }

    /// <summary>
    /// Tracks rate-changed files (.osu and audio) for later cleanup.
    /// </summary>
    private void TrackRateChangedFiles(string osuPath, OsuFile osuFile, List<string> tempRateChangedFiles)
    {
        // Track the .osu file
        if (!tempRateChangedFiles.Contains(osuPath))
        {
            tempRateChangedFiles.Add(osuPath);
            Logger.Info($"[Marathon] Tracking temp rate file: {osuPath}");
        }
        
        // Track the audio file
        var audioPath = IOPath.Combine(osuFile.DirectoryPath, osuFile.AudioFilename);
        if (!tempRateChangedFiles.Contains(audioPath))
        {
            tempRateChangedFiles.Add(audioPath);
            Logger.Info($"[Marathon] Tracking temp rate audio: {audioPath}");
        }
    }

    /// <summary>
    /// Cleans up temporary rate-changed files created during marathon creation.
    /// </summary>
    private void CleanupTempRateChangedFiles(List<string> tempRateChangedFiles)
    {
        foreach (var filePath in tempRateChangedFiles)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    Logger.Info($"[Marathon] Deleted temp file: {filePath}");
                }
            }
            catch (Exception ex)
            {
                Logger.Info($"[Marathon] Failed to delete temp file {filePath}: {ex.Message}");
                // Ignore cleanup errors - the file might be in use or already deleted
            }
        }
    }

    /// <summary>
    /// Creates a marathon beatmap from the given entries.
    /// </summary>
    /// <param name="entries">Ordered list of marathon entries.</param>
    /// <param name="metadata">Metadata for the marathon.</param>
    /// <param name="outputDirectory">Directory to create the marathon folder in.</param>
    /// <param name="progressCallback">Progress update callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the marathon creation.</returns>
    public async Task<MarathonCreationResult> CreateMarathonAsync(
        List<MarathonEntry> entries,
        MarathonMetadata metadata,
        string outputDirectory,
        Action<string>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0)
        {
            return new MarathonCreationResult
            {
                Success = false,
                ErrorMessage = "No entries provided."
            };
        }

        // Track temporary rate-changed files that should be deleted after marathon creation
        var tempRateChangedFiles = new List<string>();

        try
        {
            // Create temp directory for intermediate files
            var tempDir = IOPath.Combine(IOPath.GetTempPath(), $"companella_marathon_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                // Step 1: Pre-calculate fade durations for all entries
                // We need to do this first because pause reduction depends on BOTH adjacent maps
                progressCallback?.Invoke("Calculating audio transitions...");
                var fadeInfo = await CalculateFadeInfoAsync(entries, tempDir, tempRateChangedFiles, progressCallback, cancellationToken);

                var processedSegments = new List<ProcessedMapSegment>();
                double cumulativeOffset = 0;

                // Step 2: Process each entry with pre-calculated fade info
                for (int i = 0; i < entries.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entry = entries[i];
                    var info = fadeInfo[i];

                    // Handle pause entries - they may be partially or fully consumed
                    if (entry.IsPause)
                    {
                        var remainingPauseDuration = info.RemainingPauseDurationMs;
                        
                        if (remainingPauseDuration <= 0)
                        {
                            // Pause is fully consumed by adjacent map fades - skip it
                            Logger.Info($"[Marathon] Pause {i} fully consumed by fades");
                            continue;
                        }

                        // Generate reduced silent audio
                        progressCallback?.Invoke($"Processing pause {i + 1}/{entries.Count}: {remainingPauseDuration:F0}ms remaining");
                        
                        var segment = new ProcessedMapSegment
                        {
                            Entry = entry,
                            OffsetInMarathon = cumulativeOffset,
                            Duration = remainingPauseDuration
                        };

                        var silentAudioPath = IOPath.Combine(tempDir, $"pause_{entry.Id}.mp3");
                        await GenerateSilentAudioAsync(silentAudioPath, remainingPauseDuration / 1000.0, cancellationToken);
                        segment.TrimmedAudioPath = silentAudioPath;

                        // Use actual audio duration to avoid timing drift
                        var actualPauseDuration = await GetAudioDurationAsync(silentAudioPath, cancellationToken);
                        if (actualPauseDuration > 0)
                        {
                            segment.Duration = actualPauseDuration;
                            Logger.Info($"[Marathon] Pause: using actual duration {actualPauseDuration:F0}ms (requested {remainingPauseDuration:F0}ms)");
                        }

                        segment.ShiftedTimingPointLines.Add($"{cumulativeOffset.ToString("0.###", CultureInfo.InvariantCulture)},500,4,2,0,100,1,0");

                        processedSegments.Add(segment);
                        cumulativeOffset += segment.Duration;
                        continue;
                    }

                    // Process map entry with fades
                    progressCallback?.Invoke($"Processing map {i + 1}/{entries.Count}: {entry.Title}");

                    var mapSegment = await ProcessEntryWithFadesAsync(
                        entry, tempDir, cumulativeOffset,
                        info.IntroExtensionMs, info.IntroFadeDurationMs,
                        info.OutroExtensionMs, info.OutroFadeDurationMs,
                        tempRateChangedFiles, progressCallback, cancellationToken);

                    processedSegments.Add(mapSegment);
                    cumulativeOffset += mapSegment.Duration;
                }

                // Step 3: Concatenate audio files (skip empty paths)
                progressCallback?.Invoke("Concatenating audio files...");
                var audioFiles = processedSegments
                    .Where(s => !string.IsNullOrEmpty(s.TrimmedAudioPath) && File.Exists(s.TrimmedAudioPath))
                    .Select(s => s.TrimmedAudioPath)
                    .ToList();
                var outputFolderName = SanitizeFileName($"{metadata.Artist} - {metadata.Title}");
                var outputFolder = IOPath.Combine(outputDirectory, outputFolderName);
                Directory.CreateDirectory(outputFolder);

                var finalAudioPath = IOPath.Combine(outputFolder, "audio.mp3");
                await ConcatenateAudioFilesAsync(audioFiles, finalAudioPath, cancellationToken);

                // Step 3: Merge .osu content
                progressCallback?.Invoke("Merging beatmap content...");
                var finalOsuPath = CreateMergedOsuFile(processedSegments, metadata, outputFolder, finalAudioPath);

                // Step 4: Normalize scroll velocity
                progressCallback?.Invoke("Normalizing scroll velocity...");
                NormalizeSV(finalOsuPath);

                // Step 5: Generate composite background and highlight images
                progressCallback?.Invoke("Generating marathon background...");
                var highlightInfo = await GenerateMarathonBackgroundAsync(entries, metadata, outputFolder, cancellationToken);

                // Step 6: Inject storyboard events into the .osu file
                progressCallback?.Invoke("Adding storyboard to beatmap...");
                InjectStoryboardIntoOsuFile(finalOsuPath, processedSegments, highlightInfo);

                // Step 7: Generate marathon structure documentation
                progressCallback?.Invoke("Generating structure documentation...");
                GenerateStructureDocument(processedSegments, metadata, outputFolder);

                // Cleanup temp directory
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }

                // Cleanup temporary rate-changed files
                CleanupTempRateChangedFiles(tempRateChangedFiles);

                return new MarathonCreationResult
                {
                    Success = true,
                    OutputPath = finalOsuPath,
                    TotalDurationMs = cumulativeOffset
                };
            }
            catch
            {
                // Cleanup temp directory on error
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }

                // Cleanup temporary rate-changed files
                CleanupTempRateChangedFiles(tempRateChangedFiles);
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            return new MarathonCreationResult
            {
                Success = false,
                ErrorMessage = "Operation was cancelled."
            };
        }
        catch (Exception ex)
        {
            return new MarathonCreationResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Pre-calculates fade durations for all entries.
    /// This determines how much audio to include before/after notes for fading,
    /// and how much pause duration remains after fades consume it.
    /// </summary>
    private async Task<List<EntryFadeInfo>> CalculateFadeInfoAsync(
        List<MarathonEntry> entries,
        string tempDir,
        List<string> tempRateChangedFiles,
        Action<string>? progressCallback,
        CancellationToken cancellationToken)
    {
        var fadeInfo = new List<EntryFadeInfo>();
        
        // First pass: collect audio availability info for each map
        var mapAudioInfo = new Dictionary<int, (double FirstNoteTime, double LastNoteTime, double AudioDuration, OsuFile WorkingOsu)>();
        
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.IsPause)
            {
                fadeInfo.Add(new EntryFadeInfo { Index = i, IsPause = true, RemainingPauseDurationMs = entry.PauseDurationSeconds * 1000 });
                continue;
            }

            // Get the working osu file (after rate change if needed)
            OsuFile workingOsu = entry.OsuFile!;
            if (Math.Abs(entry.Rate - 1.0) > 0.01)
            {
                // Create rate-changed version (temporary - will be deleted after marathon creation)
                var rateChangedPath = await _rateChanger.CreateRateChangedBeatmapAsync(
                    entry.OsuFile!,
                    entry.Rate,
                    "[[name]] [[rate]]",
                    pitchAdjust: true,
                    progressCallback: null);
                workingOsu = _fileParser.Parse(rateChangedPath);
                
                // Track the .osu file and its audio for cleanup
                TrackRateChangedFiles(rateChangedPath, workingOsu, tempRateChangedFiles);
            }

            var hitObjects = ParseHitObjects(workingOsu);
            double firstNoteTime = hitObjects.Count > 0 ? hitObjects.Min(h => h.Time) : 0;
            double lastNoteTime = hitObjects.Count > 0 ? hitObjects.Max(h => h.EndTime) : 0;

            var audioPath = IOPath.Combine(workingOsu.DirectoryPath, workingOsu.AudioFilename);
            var audioDuration = await GetAudioDurationAsync(audioPath, cancellationToken);

            mapAudioInfo[i] = (firstNoteTime, lastNoteTime, audioDuration, workingOsu);
            fadeInfo.Add(new EntryFadeInfo { Index = i, IsPause = false });
        }

        // Second pass: calculate fade durations based on adjacent pauses
        // We track both:
        // - Extension: how much extra audio to include (trim boundary change)
        // - Fade duration: how long the actual fade effect is (may include note audio if no extra available)
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var info = fadeInfo[i];

            if (entry.IsPause)
            {
                // Calculate how much of this pause is consumed by adjacent maps' fades
                double consumedByPrevious = 0;
                double consumedByNext = 0;
                var pauseDurationMs = entry.PauseDurationSeconds * 1000;

                // Check previous entry (map that fades out into this pause)
                if (i > 0 && !entries[i - 1].IsPause && mapAudioInfo.ContainsKey(i - 1))
                {
                    var prevInfo = mapAudioInfo[i - 1];
                    var prevFadeInfo = fadeInfo[i - 1];
                    
                    // Available outro = audio after last note
                    var availableOutro = Math.Max(0, prevInfo.AudioDuration - prevInfo.LastNoteTime);
                    
                    // Extension is limited by available audio
                    var outroExtension = Math.Min(pauseDurationMs, availableOutro);
                    prevFadeInfo.OutroExtensionMs = outroExtension;
                    
                    // Fade duration: ONLY fade audio that exists AFTER the last note
                    // Never fade out while notes are still playing - if no audio after notes, no fade at all
                    prevFadeInfo.OutroFadeDurationMs = outroExtension;
                    
                    // The pause is consumed by the extension
                    consumedByPrevious = outroExtension;
                    
                    Logger.Info($"[Marathon] Map {i-1} outro: {availableOutro:F0}ms available, extension={outroExtension:F0}ms, fade={outroExtension:F0}ms");
                }

                // Check next entry (map that fades in from this pause)
                if (i < entries.Count - 1 && !entries[i + 1].IsPause && mapAudioInfo.ContainsKey(i + 1))
                {
                    var nextInfo = mapAudioInfo[i + 1];
                    var nextFadeInfo = fadeInfo[i + 1];
                    
                    // Available intro = audio before first note
                    var availableIntro = nextInfo.FirstNoteTime;
                    
                    // Use remaining pause duration after previous consumption
                    var remainingForNext = pauseDurationMs - consumedByPrevious;
                    
                    // Extension is limited by available audio and remaining pause
                    var introExtension = Math.Min(remainingForNext, availableIntro);
                    nextFadeInfo.IntroExtensionMs = introExtension;
                    
                    // Fade duration: ONLY fade audio that exists BEFORE the first note
                    // Never fade while notes are playing - if no audio before notes, no fade at all
                    nextFadeInfo.IntroFadeDurationMs = introExtension;
                    
                    // The pause is "consumed" by the extension
                    consumedByNext = introExtension;
                    
                    Logger.Info($"[Marathon] Map {i+1} intro: {availableIntro:F0}ms available, extension={introExtension:F0}ms, fade={introExtension:F0}ms");
                }

                // Remaining pause duration (only extensions consume the pause, not boundary fades)
                info.RemainingPauseDurationMs = pauseDurationMs - consumedByPrevious - consumedByNext;
                
                Logger.Info($"[Marathon] Pause {i}: {pauseDurationMs:F0}ms total, {consumedByPrevious:F0}ms consumed by prev, {consumedByNext:F0}ms consumed by next, {info.RemainingPauseDurationMs:F0}ms remaining");
            }
        }

        // Edge cases: pauses at start or end get full duration for fading
        // (already handled above - if no adjacent map, consumed stays 0)

        return fadeInfo;
    }

    /// <summary>
    /// Processes a map entry with support for fade transitions during pauses.
    /// Pause entries are handled separately in the main loop.
    /// </summary>
    /// <param name="entry">The marathon entry to process (must be a map, not pause).</param>
    /// <param name="tempDir">Temporary directory for intermediate files.</param>
    /// <param name="offsetInMarathon">Current offset in the marathon timeline.</param>
    /// <param name="introExtensionMs">How much extra audio to include before first note (ms).</param>
    /// <param name="introFadeDurationMs">Duration of fade-in effect (equals extension - only fade audio before notes).</param>
    /// <param name="outroExtensionMs">How much extra audio to include after last note (ms).</param>
    /// <param name="outroFadeDurationMs">Duration of fade-out effect (equals extension - only fade audio after notes).</param>
    /// <param name="tempRateChangedFiles">List to track temporary rate-changed files for cleanup.</param>
    /// <param name="progressCallback">Progress callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task<ProcessedMapSegment> ProcessEntryWithFadesAsync(
        MarathonEntry entry,
        string tempDir,
        double offsetInMarathon,
        double introExtensionMs,
        double introFadeDurationMs,
        double outroExtensionMs,
        double outroFadeDurationMs,
        List<string> tempRateChangedFiles,
        Action<string>? progressCallback,
        CancellationToken cancellationToken)
    {
        var segment = new ProcessedMapSegment
        {
            Entry = entry,
            OffsetInMarathon = offsetInMarathon
        };

        OsuFile workingOsuFile = entry.OsuFile!;

        // If rate is not 1.0, create a rate-changed version (will reuse existing if already created)
        if (Math.Abs(entry.Rate - 1.0) > 0.01)
        {
            progressCallback?.Invoke($"  Creating {entry.Rate:0.0#}x rate version...");
            var rateChangedPath = await _rateChanger.CreateRateChangedBeatmapAsync(
                entry.OsuFile!,
                entry.Rate,
                "[[name]] [[rate]]",
                pitchAdjust: true,
                progressCallback: null);
            workingOsuFile = _fileParser.Parse(rateChangedPath);
            
            // Track for cleanup (may already be tracked from CalculateFadeInfoAsync, but that's fine)
            TrackRateChangedFiles(rateChangedPath, workingOsuFile, tempRateChangedFiles);
        }

        // Parse hit objects to get accurate first/last note times
        var hitObjects = ParseHitObjects(workingOsuFile);
        double firstNoteTime = hitObjects.Count > 0 ? hitObjects.Min(h => h.Time) : 0;
        double lastNoteEndTime = hitObjects.Count > 0 ? hitObjects.Max(h => h.EndTime) : 0;

        var originalAudioPath = IOPath.Combine(workingOsuFile.DirectoryPath, workingOsuFile.AudioFilename);

        // Calculate trim boundaries - only extend by the EXTENSION amount (actual extra audio)
        // The fade duration may be longer (boundary fade into note audio)
        double trimStartMs = firstNoteTime - introExtensionMs;
        double trimEndMs = lastNoteEndTime + outroExtensionMs;

        segment.IntroFadeDuration = introExtensionMs;
        segment.OutroFadeDuration = outroExtensionMs;

        // The segment duration includes only the extension portions (not boundary fades)
        double noteDuration = lastNoteEndTime - firstNoteTime;
        segment.Duration = introExtensionMs + noteDuration + outroExtensionMs;

        // Trim audio with extended boundaries and apply fades
        // Fade duration may exceed extension (boundary fade into note audio)
        progressCallback?.Invoke($"  Trimming audio (ext: {introExtensionMs:F0}/{outroExtensionMs:F0}ms, fade: {introFadeDurationMs:F0}/{outroFadeDurationMs:F0}ms)...");
        var trimmedAudioPath = IOPath.Combine(tempDir, $"trimmed_{entry.Id}.mp3");
        await TrimAudioWithFadesAsync(
            originalAudioPath, trimmedAudioPath,
            trimStartMs, trimEndMs,
            introFadeDurationMs, outroFadeDurationMs,
            cancellationToken);
        segment.TrimmedAudioPath = trimmedAudioPath;

        // CRITICAL: Use the ACTUAL audio file duration to avoid timing drift
        // MP3 encoding can cause slight duration differences from our calculations
        var actualAudioDuration = await GetAudioDurationAsync(trimmedAudioPath, cancellationToken);
        if (actualAudioDuration > 0)
        {
            var calculatedDuration = segment.Duration;
            var durationDiff = Math.Abs(actualAudioDuration - calculatedDuration);
            if (durationDiff > 100) // More than 100ms difference is suspicious
            {
                Logger.Info($"[Marathon] WARNING: Audio duration mismatch for {entry.Title}: calculated={calculatedDuration:F0}ms, actual={actualAudioDuration:F0}ms");
            }
            segment.Duration = actualAudioDuration;
            Logger.Info($"[Marathon] {entry.Title}: using actual audio duration {actualAudioDuration:F0}ms (calculated was {calculatedDuration:F0}ms)");
        }

        // Shift all times in the .osu data
        // The trimmed audio starts at (firstNoteTime - introExtensionMs) in the original
        // This maps to offsetInMarathon in the marathon
        // The first note will be at offsetInMarathon + introExtensionMs
        progressCallback?.Invoke($"  Adjusting timing data...");
        var trimStartTime = firstNoteTime - introExtensionMs;
        var firstNoteInMarathon = offsetInMarathon + introExtensionMs;
        ShiftAndCollectOsuData(workingOsuFile, trimStartTime, offsetInMarathon, firstNoteInMarathon, segment);

        return segment;
    }

    /// <summary>
    /// Generates silent audio of the specified duration.
    /// </summary>
    private async Task GenerateSilentAudioAsync(
        string outputPath,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        // Use ffmpeg to generate silent audio
        // -f lavfi -i anullsrc generates silence
        // -t sets duration
        var arguments = $"-y -f lavfi -i anullsrc=r=44100:cl=stereo -t {durationSeconds.ToString("0.###", CultureInfo.InvariantCulture)} -c:a libmp3lame -q:a 2 \"{outputPath}\"";

        Logger.Info($"[Marathon] Generating silent audio: {_ffmpegPath} {arguments}");

        await RunFfmpegAsync(arguments, cancellationToken);

        if (!File.Exists(outputPath))
            throw new InvalidOperationException($"ffmpeg did not create silent audio: {outputPath}");
    }

    /// <summary>
    /// Gets the duration of an audio file in milliseconds.
    /// Uses ffmpeg to analyze the file (more reliable than ffprobe).
    /// </summary>
    private async Task<double> GetAudioDurationAsync(string audioPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(audioPath))
            return 0;

        // First try ffprobe (more accurate)
        var duration = await TryGetDurationWithFfprobeAsync(audioPath, cancellationToken);
        if (duration > 0)
            return duration;

        // Fallback: use ffmpeg to get duration from stderr output
        duration = await TryGetDurationWithFfmpegAsync(audioPath, cancellationToken);
        if (duration > 0)
            return duration;

        // Last resort: estimate from file size (rough approximation for MP3 at ~192kbps)
        try
        {
            var fileInfo = new FileInfo(audioPath);
            // Rough estimate: 192kbps = 24KB/s
            var estimated = (fileInfo.Length / 24000.0) * 1000;
            Logger.Info($"[Marathon] WARNING: Using file size estimate for duration: {estimated:F0}ms");
            return estimated;
        }
        catch
        {
            Logger.Info($"[Marathon] ERROR: Could not determine audio duration for {audioPath}");
            return 0;
        }
    }

    private async Task<double> TryGetDurationWithFfprobeAsync(string audioPath, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = $"-i \"{audioPath}\" -show_entries format=duration -v quiet -of csv=\"p=0\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            var outputBuilder = new StringBuilder();

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                    outputBuilder.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();

            var completed = await Task.Run(() => process.WaitForExit(10000), cancellationToken);

            if (!completed)
            {
                try { process.Kill(); } catch { }
                return 0;
            }

            if (process.ExitCode == 0)
            {
                var output = outputBuilder.ToString().Trim();
                if (double.TryParse(output, NumberStyles.Float, CultureInfo.InvariantCulture, out var durationSec))
                {
                    return durationSec * 1000;
                }
            }
        }
        catch { }
        return 0;
    }

    private async Task<double> TryGetDurationWithFfmpegAsync(string audioPath, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = $"-i \"{audioPath}\" -f null -",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            var errorBuilder = new StringBuilder();

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                    errorBuilder.AppendLine(e.Data);
            };

            process.Start();
            process.BeginErrorReadLine();

            var completed = await Task.Run(() => process.WaitForExit(10000), cancellationToken);

            if (!completed)
            {
                try { process.Kill(); } catch { }
                return 0;
            }

            // Parse duration from ffmpeg output: "Duration: 00:03:45.67"
            var output = errorBuilder.ToString();
            var match = Regex.Match(output, @"Duration:\s*(\d+):(\d+):(\d+)\.(\d+)");
            if (match.Success)
            {
                var hours = int.Parse(match.Groups[1].Value);
                var minutes = int.Parse(match.Groups[2].Value);
                var seconds = int.Parse(match.Groups[3].Value);
                var centiseconds = int.Parse(match.Groups[4].Value.PadRight(2, '0').Substring(0, 2));
                
                return (hours * 3600 + minutes * 60 + seconds) * 1000 + centiseconds * 10;
            }
        }
        catch { }
        return 0;
    }

    /// <summary>
    /// Trims audio and applies fade-in/fade-out in a single operation.
    /// </summary>
    private async Task TrimAudioWithFadesAsync(
        string inputPath,
        string outputPath,
        double startMs,
        double endMs,
        double fadeInDurationMs,
        double fadeOutDurationMs,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"Audio file not found: {inputPath}");

        var startSec = startMs / 1000.0;
        var endSec = endMs / 1000.0;
        var duration = endSec - startSec;

        // Build ffmpeg command with trim and fades
        var filterParts = new List<string>();

        // Fade-in at the start (clamp to audio duration)
        if (fadeInDurationMs > 0)
        {
            var fadeInSec = Math.Min(fadeInDurationMs / 1000.0, duration);
            filterParts.Add($"afade=t=in:st=0:d={fadeInSec.ToString("0.###", CultureInfo.InvariantCulture)}");
        }

        // Fade-out at the end (ensure start is not negative)
        if (fadeOutDurationMs > 0)
        {
            var fadeOutSec = Math.Min(fadeOutDurationMs / 1000.0, duration);
            var fadeOutStart = Math.Max(0, duration - fadeOutSec);
            filterParts.Add($"afade=t=out:st={fadeOutStart.ToString("0.###", CultureInfo.InvariantCulture)}:d={fadeOutSec.ToString("0.###", CultureInfo.InvariantCulture)}");
        }

        string arguments;
        // IMPORTANT: -ss and -t MUST be BEFORE -i for input seeking!
        // When -ss is after -i, it's "output seeking" which applies filters to the full audio BEFORE seeking,
        // causing fade timings to be completely wrong.
        // Using -t (duration) instead of -to (end position) for clarity with input seeking.
        var durationSec = duration.ToString("0.###", CultureInfo.InvariantCulture);
        var startSecStr = startSec.ToString("0.###", CultureInfo.InvariantCulture);
        
        if (filterParts.Count > 0)
        {
            var filterChain = string.Join(",", filterParts);
            arguments = $"-y -ss {startSecStr} -t {durationSec} -i \"{inputPath}\" -af \"{filterChain}\" -c:a libmp3lame -q:a 2 \"{outputPath}\"";
        }
        else
        {
            // No fades, just trim
            arguments = $"-y -ss {startSecStr} -t {durationSec} -i \"{inputPath}\" -c:a libmp3lame -q:a 2 \"{outputPath}\"";
        }

        Logger.Info($"[Marathon] Trimming with fades: {_ffmpegPath} {arguments}");

        await RunFfmpegAsync(arguments, cancellationToken);

        if (!File.Exists(outputPath))
            throw new InvalidOperationException($"ffmpeg did not create trimmed audio: {outputPath}");
    }

    /// <summary>
    /// Parses hit objects from an OsuFile.
    /// </summary>
    private List<HitObject> ParseHitObjects(OsuFile osuFile)
    {
        var hitObjects = new List<HitObject>();
        if (!osuFile.RawSections.TryGetValue("HitObjects", out var lines))
            return hitObjects;

        var keyCount = (int)osuFile.CircleSize;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                continue;
            var hitObj = HitObject.Parse(line, keyCount);
            if (hitObj != null)
                hitObjects.Add(hitObj);
        }

        return hitObjects;
    }

    /// <summary>
    /// Concatenates multiple audio files into one.
    /// </summary>
    private async Task ConcatenateAudioFilesAsync(
        List<string> inputPaths,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (inputPaths.Count == 0)
            throw new ArgumentException("No audio files to concatenate.");

        if (inputPaths.Count == 1)
        {
            // Just copy the single file
            File.Copy(inputPaths[0], outputPath, true);
            return;
        }

        // Create a concat list file
        var concatListPath = IOPath.Combine(IOPath.GetDirectoryName(inputPaths[0])!, "concat_list.txt");
        var concatContent = new StringBuilder();
        foreach (var path in inputPaths)
        {
            // Escape single quotes in path and use proper format
            var escapedPath = path.Replace("'", "'\\''");
            concatContent.AppendLine($"file '{escapedPath}'");
        }
        await File.WriteAllTextAsync(concatListPath, concatContent.ToString(), cancellationToken);

        // Use concat demuxer
        var arguments = $"-y -f concat -safe 0 -i \"{concatListPath}\" -c:a libmp3lame -q:a 2 \"{outputPath}\"";

        Logger.Info($"[Marathon] Concatenating: {_ffmpegPath} {arguments}");

        await RunFfmpegAsync(arguments, cancellationToken);

        // Cleanup concat list
        try { File.Delete(concatListPath); } catch { }

        if (!File.Exists(outputPath))
            throw new InvalidOperationException($"ffmpeg did not create concatenated audio: {outputPath}");
    }

    /// <summary>
    /// Runs ffmpeg with the given arguments.
    /// </summary>
    private async Task RunFfmpegAsync(string arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        var errorBuilder = new StringBuilder();

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
                errorBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginErrorReadLine();

        var completed = await Task.Run(() =>
        {
            try
            {
                return process.WaitForExit(300000); // 5 minute timeout
            }
            catch
            {
                return false;
            }
        }, cancellationToken);

        if (!completed)
        {
            try { process.Kill(); } catch { }
            throw new TimeoutException("ffmpeg timed out after 5 minutes");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg failed with exit code {process.ExitCode}:\n{errorBuilder}");
        }
    }

    /// <summary>
    /// Shifts times in the .osu data and collects the modified lines.
    /// </summary>
    /// <param name="osuFile">The osu file to process.</param>
    /// <param name="trimStartTime">The start time of the trimmed audio in the original file.</param>
    /// <param name="segmentStart">Where this segment's audio starts in the marathon.</param>
    /// <param name="firstNoteInMarathon">Where the first note of this segment is in the marathon.</param>
    /// <param name="segment">The segment to collect data into.</param>
    private void ShiftAndCollectOsuData(
        OsuFile osuFile,
        double trimStartTime,
        double segmentStart,
        double firstNoteInMarathon,
        ProcessedMapSegment segment)
    {
        // Shift HitObjects
        if (osuFile.RawSections.TryGetValue("HitObjects", out var hitObjectLines))
        {
            foreach (var line in hitObjectLines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                    continue;

                var shifted = ShiftHitObjectLine(line, trimStartTime, segmentStart);
                if (shifted != null)
                    segment.ShiftedHitObjectLines.Add(shifted);
            }
        }

        // Process TimingPoints - need special handling for points before trim start
        ProcessTimingPoints(osuFile, trimStartTime, segmentStart, firstNoteInMarathon, segment);

        // Shift Events (breaks, etc.)
        if (osuFile.RawSections.TryGetValue("Events", out var eventLines))
        {
            foreach (var line in eventLines)
            {
                var shifted = ShiftEventLine(line, trimStartTime, segmentStart);
                segment.ShiftedEventLines.Add(shifted);
            }
        }
    }

    /// <summary>
    /// Processes timing points for a segment, handling points before the trim start correctly.
    /// - Keep only the LAST uninherited (BPM) point before trim start, place it at the first note time
    /// - Discard all other timing points before trim start
    /// - Shift timing points after trim start normally
    /// </summary>
    /// <param name="osuFile">The osu file to process.</param>
    /// <param name="trimStartTime">The start time of the trimmed audio in the original file.</param>
    /// <param name="segmentStart">Where this segment's audio starts in the marathon.</param>
    /// <param name="firstNoteInMarathon">Where the first note of this segment is in the marathon.</param>
    /// <param name="segment">The segment to collect data into.</param>
    private void ProcessTimingPoints(
        OsuFile osuFile,
        double trimStartTime,
        double segmentStart,
        double firstNoteInMarathon,
        ProcessedMapSegment segment)
    {
        var timingPoints = osuFile.TimingPoints.OrderBy(tp => tp.Time).ToList();
        
        if (timingPoints.Count == 0)
        {
            // No timing points - create a default one at the first note
            segment.ShiftedTimingPointLines.Add($"{firstNoteInMarathon.ToString("0.###", CultureInfo.InvariantCulture)},500,4,2,0,100,1,0");
            return;
        }

        // Find the LAST uninherited (BPM) timing point before or at trimStartTime
        // This is the BPM that's active when the trimmed section starts
        TimingPoint? lastBpmBeforeTrim = null;
        foreach (var tp in timingPoints)
        {
            if (tp.Uninherited && tp.Time <= trimStartTime)
            {
                lastBpmBeforeTrim = tp;
            }
        }

        // If no BPM point found before trim start, use the first uninherited point in the file
        if (lastBpmBeforeTrim == null)
        {
            lastBpmBeforeTrim = timingPoints.FirstOrDefault(tp => tp.Uninherited);
        }

        // Add the BPM point at the exact first note time in the marathon
        if (lastBpmBeforeTrim != null)
        {
            var bpmAtFirstNote = new TimingPoint
            {
                Time = firstNoteInMarathon,
                BeatLength = lastBpmBeforeTrim.BeatLength,
                Meter = lastBpmBeforeTrim.Meter,
                SampleSet = lastBpmBeforeTrim.SampleSet,
                SampleIndex = lastBpmBeforeTrim.SampleIndex,
                Volume = lastBpmBeforeTrim.Volume,
                Uninherited = true,
                Effects = lastBpmBeforeTrim.Effects
            };
            segment.ShiftedTimingPointLines.Add(bpmAtFirstNote.ToString());
            Logger.Info($"[Marathon] Added BPM point at first note: {firstNoteInMarathon:F0}ms, BPM={60000.0 / lastBpmBeforeTrim.BeatLength:F1}");
        }

        // Now process timing points that are AFTER trimStartTime
        // All timing points before trimStartTime are discarded (except the BPM we just added)
        foreach (var tp in timingPoints)
        {
            // Skip all points at or before trim start - they're from the trimmed section
            if (tp.Time <= trimStartTime)
            {
                continue;
            }

            // Shift the timing point: original time maps to marathon time
            var newTime = tp.Time - trimStartTime + segmentStart;

            var shiftedPoint = new TimingPoint
            {
                Time = newTime,
                BeatLength = tp.BeatLength,
                Meter = tp.Meter,
                SampleSet = tp.SampleSet,
                SampleIndex = tp.SampleIndex,
                Volume = tp.Volume,
                Uninherited = tp.Uninherited,
                Effects = tp.Effects
            };
            segment.ShiftedTimingPointLines.Add(shiftedPoint.ToString());
        }
    }

    /// <summary>
    /// Shifts a hit object line by the given offset.
    /// </summary>
    /// <param name="line">The hit object line to shift.</param>
    /// <param name="trimStartTime">The start time of the trimmed audio in the original file.</param>
    /// <param name="segmentStart">Where this segment starts in the marathon.</param>
    private string? ShiftHitObjectLine(string line, double trimStartTime, double segmentStart)
    {
        var parts = line.Split(',');
        if (parts.Length < 5) return null;

        try
        {
            // Parse time (index 2)
            var time = double.Parse(parts[2], CultureInfo.InvariantCulture);
            var newTime = time - trimStartTime + segmentStart;
            
            // Hit objects should never be before segment start (they should all be after trim start)
            if (newTime < segmentStart)
            {
                Logger.Info($"[Marathon] WARNING: Hit object at {time}ms is before trim start {trimStartTime}ms");
                newTime = segmentStart;
            }
            
            parts[2] = ((int)Math.Round(newTime)).ToString(CultureInfo.InvariantCulture);

            // Check for hold note end time (in parts[5] before colon)
            if (parts.Length >= 6 && parts[5].Contains(':'))
            {
                var endParts = parts[5].Split(':');
                if (long.TryParse(endParts[0], out var endTime))
                {
                    var newEndTime = endTime - trimStartTime + segmentStart;
                    if (newEndTime < segmentStart) newEndTime = segmentStart;
                    endParts[0] = ((long)Math.Round(newEndTime)).ToString(CultureInfo.InvariantCulture);
                    parts[5] = string.Join(":", endParts);
                }
            }

            return string.Join(",", parts);
        }
        catch
        {
            return line;
        }
    }

    /// <summary>
    /// Shifts an event line by the given offset (for breaks, etc.).
    /// </summary>
    /// <param name="line">The event line to shift.</param>
    /// <param name="trimStartTime">The start time of the trimmed audio in the original file.</param>
    /// <param name="segmentStart">Where this segment starts in the marathon.</param>
    private string ShiftEventLine(string line, double trimStartTime, double segmentStart)
    {
        // Background images - keep as-is (will need to handle separately)
        if (line.StartsWith("0,0,"))
            return line;

        // Video - skip for marathon
        if (line.StartsWith("Video,") || line.StartsWith("1,"))
            return "// " + line;

        // Break: 2,startTime,endTime
        if (line.StartsWith("2,") || line.StartsWith("Break,"))
        {
            var parts = line.Split(',');
            if (parts.Length >= 3)
            {
                try
                {
                    if (int.TryParse(parts[1], out var startTime))
                    {
                        var newStart = startTime - trimStartTime + segmentStart;
                        // Skip breaks that are entirely before trim start
                        if (newStart < segmentStart) newStart = segmentStart;
                        parts[1] = ((int)newStart).ToString();
                    }
                    if (int.TryParse(parts[2], out var endTime))
                    {
                        var newEnd = endTime - trimStartTime + segmentStart;
                        if (newEnd < segmentStart) newEnd = segmentStart;
                        parts[2] = ((int)newEnd).ToString();
                    }
                    return string.Join(",", parts);
                }
                catch { }
            }
        }

        return line;
    }

    /// <summary>
    /// Creates the merged .osu file from all processed segments.
    /// </summary>
    private string CreateMergedOsuFile(
        List<ProcessedMapSegment> segments,
        MarathonMetadata metadata,
        string outputFolder,
        string audioFilename)
    {
        var lines = new List<string>();

        // Get reference from first non-pause segment for format version and other settings
        var firstMapSegment = segments.FirstOrDefault(s => !s.Entry.IsPause);
        var firstOsu = firstMapSegment?.Entry.OsuFile;

        // osu file format version
        lines.Add("osu file format v14");
        lines.Add("");

        // [General]
        lines.Add("[General]");
        lines.Add($"AudioFilename: {IOPath.GetFileName(audioFilename)}");
        lines.Add("AudioLeadIn: 0");
        lines.Add("PreviewTime: 0");
        lines.Add("Countdown: 0");
        lines.Add("SampleSet: Soft");
        lines.Add("StackLeniency: 0.7");
        lines.Add($"Mode: {firstOsu?.Mode ?? 3}"); // Default to mania mode
        lines.Add("LetterboxInBreaks: 0");
        lines.Add("SpecialStyle: 0");
        lines.Add("WidescreenStoryboard: 0");
        lines.Add("");

        // [Editor]
        lines.Add("[Editor]");
        lines.Add("DistanceSpacing: 1");
        lines.Add("BeatDivisor: 4");
        lines.Add("GridSize: 4");
        lines.Add("TimelineZoom: 1");
        lines.Add("");

        // [Metadata]
        lines.Add("[Metadata]");
        lines.Add($"Title:{metadata.Title}");
        lines.Add($"TitleUnicode:{(string.IsNullOrEmpty(metadata.TitleUnicode) ? metadata.Title : metadata.TitleUnicode)}");
        lines.Add($"Artist:{metadata.Artist}");
        lines.Add($"ArtistUnicode:{(string.IsNullOrEmpty(metadata.ArtistUnicode) ? metadata.Artist : metadata.ArtistUnicode)}");
        lines.Add($"Creator:{metadata.Creator}");
        lines.Add($"Version:{metadata.Version}");
        lines.Add("Source:");
        lines.Add($"Tags:{metadata.Tags}");
        lines.Add("BeatmapID:0");
        lines.Add("BeatmapSetID:-1");
        lines.Add("");

        // [Difficulty]
        lines.Add("[Difficulty]");
        lines.Add($"HPDrainRate:{firstOsu?.HPDrainRate ?? 5}");
        lines.Add($"CircleSize:{firstOsu?.CircleSize ?? 4}");
        lines.Add($"OverallDifficulty:{firstOsu?.OverallDifficulty ?? 8}");
        lines.Add($"ApproachRate:{firstOsu?.ApproachRate ?? 5}");
        lines.Add($"SliderMultiplier:{firstOsu?.SliderMultiplier ?? 1.4}");
        lines.Add($"SliderTickRate:{firstOsu?.SliderTickRate ?? 1}");
        lines.Add("");

        // [Events]
        lines.Add("[Events]");
        lines.Add("//Background and Video events");
        // Reference the generated composite background (will be created after this file)
        lines.Add("0,0,\"background.jpg\",0,0");
        lines.Add("//Break Periods");
        // Collect all breaks from segments
        foreach (var segment in segments)
        {
            foreach (var eventLine in segment.ShiftedEventLines)
            {
                if (eventLine.StartsWith("2,") || eventLine.StartsWith("Break,"))
                    lines.Add(eventLine);
            }
        }
        lines.Add("//Storyboard Layer 0 (Background)");
        lines.Add("//Storyboard Layer 1 (Fail)");
        lines.Add("//Storyboard Layer 2 (Pass)");
        lines.Add("//Storyboard Layer 3 (Foreground)");
        lines.Add("//Storyboard Layer 4 (Overlay)");
        lines.Add("//Storyboard Sound Samples");
        lines.Add("");

        // [TimingPoints]
        lines.Add("[TimingPoints]");
        foreach (var segment in segments)
        {
            foreach (var tpLine in segment.ShiftedTimingPointLines)
            {
                lines.Add(tpLine);
            }
        }
        lines.Add("");

        // [Colours] - optional, skip for now
        lines.Add("");

        // [HitObjects]
        lines.Add("[HitObjects]");
        foreach (var segment in segments)
        {
            foreach (var hoLine in segment.ShiftedHitObjectLines)
            {
                lines.Add(hoLine);
            }
        }

        // Generate filename
        var osuFileName = SanitizeFileName($"{metadata.Artist} - {metadata.Title} ({metadata.Creator}) [{metadata.Version}].osu");
        var osuPath = IOPath.Combine(outputFolder, osuFileName);

        File.WriteAllLines(osuPath, lines);

        Logger.Info($"[Marathon] Created: {osuPath}");

        return osuPath;
    }

    /// <summary>
    /// Normalizes scroll velocity in the .osu file using the SvNormalizer service.
    /// </summary>
    private void NormalizeSV(string osuPath)
    {
        if (!File.Exists(osuPath))
            return;

        try
        {
            var osuFile = _fileParser.Parse(osuPath);
            var existingTimingPoints = osuFile.TimingPoints;
            var uninheritedCount = existingTimingPoints.Count(tp => tp.Uninherited);

            if (uninheritedCount <= 1)
            {
                Logger.Info($"[Marathon] No BPM changes found - SV normalization not needed.");
                return;
            }

            var svNormalizer = new SvNormalizer();
            var fileWriter = new OsuFileWriter();

            // Determine base BPM (most common by duration)
            var uninherited = existingTimingPoints.Where(tp => tp.Uninherited).OrderBy(tp => tp.Time).ToList();
            double baseBpm = uninherited.Count > 0 ? uninherited[0].Bpm : 120;

            if (uninherited.Count > 1)
            {
                var bpmDurations = new Dictionary<double, double>();
                for (int i = 0; i < uninherited.Count; i++)
                {
                    double bpm = Math.Round(uninherited[i].Bpm, 1);
                    double duration = i < uninherited.Count - 1
                        ? uninherited[i + 1].Time - uninherited[i].Time
                        : 60000;
                    if (!bpmDurations.ContainsKey(bpm)) bpmDurations[bpm] = 0;
                    bpmDurations[bpm] += duration;
                }
                baseBpm = bpmDurations.OrderByDescending(kvp => kvp.Value).First().Key;
            }

            var normalizedTimingPoints = svNormalizer.Normalize(existingTimingPoints, baseBpm);
            var stats = svNormalizer.GetStats(existingTimingPoints, normalizedTimingPoints, baseBpm);

            fileWriter.Write(osuFile, normalizedTimingPoints);

            Logger.Info($"[Marathon] SV normalized: {osuPath}");
            Logger.Info($"[Marathon] Base BPM: {stats.BaseBpm:F0}, SV range: {stats.MinSv:F2}x - {stats.MaxSv:F2}x");
        }
        catch (Exception ex)
        {
            Logger.Info($"[Marathon] SV normalization failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Sanitizes a string for use as a filename.
    /// </summary>
    private string SanitizeFileName(string name)
    {
        var invalidChars = IOPath.GetInvalidFileNameChars();
        var sanitized = string.Join("_", name.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        return sanitized.Trim();
    }

    /// <summary>
    /// Checks if ffmpeg is available.
    /// </summary>
    public async Task<bool> CheckFfmpegAvailableAsync()
    {
        return await _rateChanger.CheckFfmpegAvailableAsync();
    }

    /// <summary>
    /// Generates a preview image of the marathon background.
    /// This is a smaller, faster version for real-time preview updates.
    /// </summary>
    /// <param name="entries">List of marathon entries.</param>
    /// <param name="centerText">Text to display in the center circle.</param>
    /// <param name="glitchIntensity">Glitch effect intensity (0.0 to 1.0).</param>
    /// <param name="previewWidth">Width of the preview image (default 480).</param>
    /// <param name="previewHeight">Height of the preview image (default 270).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Preview image as byte array (JPEG), or null if generation fails.</returns>
    public async Task<byte[]?> GenerateBackgroundPreviewAsync(
        List<MarathonEntry> entries,
        string centerText,
        float glitchIntensity,
        int previewWidth = 480,
        int previewHeight = 270,
        CancellationToken cancellationToken = default)
    {
        // Filter to only map entries (skip pauses)
        var mapEntries = entries.Where(e => !e.IsPause && e.OsuFile != null).ToList();

        if (mapEntries.Count == 0)
        {
            return null;
        }

        try
        {
            // Calculate scale factors
            float scaleX = previewWidth / (float)BgWidth;
            float scaleY = previewHeight / (float)BgHeight;
            float centerX = previewWidth / 2f;
            float centerY = previewHeight / 2f;
            float innerRadius = InnerRadius * scaleX;
            float outerRadius = OuterRadius * scaleX;
            float circleRadius = CenterCircleRadius * scaleX;
            float borderThickness = Math.Max(1f, BorderThickness * scaleX);

            // Create the preview canvas
            using var previewImage = new Image<Rgba32>(previewWidth, previewHeight);
            previewImage.Mutate(ctx => ctx.Fill(SixLabors.ImageSharp.Color.Black));

            // Calculate angle per shard
            float anglePerShard = 360f / mapEntries.Count;
            float startAngle = -90f - (anglePerShard / 2f);

            var shardPaths = new List<IPath>();

            for (int i = 0; i < mapEntries.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entry = mapEntries[i];
                float shardStartAngle = startAngle + (i * anglePerShard);
                float shardEndAngle = shardStartAngle + anglePerShard;

                // Load and resize background for preview
                using var shardImage = await LoadMapBackgroundForPreviewAsync(entry, previewWidth, previewHeight, cancellationToken);

                // Build shard path at preview scale
                var shardPath = BuildShardPathScaled(shardStartAngle, shardEndAngle, centerX, centerY, innerRadius, outerRadius);
                shardPaths.Add(shardPath);

                // Calculate shard center at preview scale
                var shardCenter = CalculateShardCenterScaled(shardStartAngle, shardEndAngle, centerX, centerY, innerRadius, outerRadius);

                // Draw shard
                DrawShardOntoCanvasScaled(previewImage, shardImage, shardPath, shardCenter, previewWidth, previewHeight);
            }

            // Draw borders
            DrawShardBordersScaled(previewImage, shardPaths, centerX, centerY, innerRadius, outerRadius, borderThickness);

            // Draw center circle with text
            DrawCenterCircleScaled(previewImage, centerText, centerX, centerY, circleRadius, borderThickness);

            // Apply glitch effects (scaled for preview resolution)
            if (glitchIntensity > 0)
            {
                // Calculate scale factor relative to full resolution
                float scale = previewWidth / (float)BgWidth;
                // Use a fixed seed for preview consistency
                ApplyGlitchEffectsScaled(previewImage, glitchIntensity, 12345, scale);
            }

            // Convert to JPEG bytes
            using var memoryStream = new MemoryStream();
            await previewImage.SaveAsJpegAsync(memoryStream, cancellationToken);
            return memoryStream.ToArray();
        }
        catch (Exception ex)
        {
            Logger.Info($"[Marathon] Preview generation failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Loads a map's background image resized for preview.
    /// </summary>
    private async Task<Image<Rgba32>> LoadMapBackgroundForPreviewAsync(MarathonEntry entry, int width, int height, CancellationToken cancellationToken)
    {
        if (entry.OsuFile?.BackgroundFilename != null)
        {
            var bgPath = IOPath.Combine(entry.OsuFile.DirectoryPath, entry.OsuFile.BackgroundFilename);
            if (File.Exists(bgPath))
            {
                try
                {
                    using var stream = File.OpenRead(bgPath);
                    var image = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(stream, cancellationToken);
                    
                    image.Mutate(ctx => ctx.Resize(new ResizeOptions
                    {
                        Size = new SixLabors.ImageSharp.Size(width, height),
                        Mode = ResizeMode.Crop,
                        Position = AnchorPositionMode.Center
                    }));
                    
                    return image;
                }
                catch
                {
                    // Fall through to placeholder
                }
            }
        }

        var placeholder = new Image<Rgba32>(width, height);
        placeholder.Mutate(ctx => ctx.Fill(SixLabors.ImageSharp.Color.Black));
        return placeholder;
    }

    /// <summary>
    /// Builds a shard path at the specified scale.
    /// </summary>
    private IPath BuildShardPathScaled(float startAngleDeg, float endAngleDeg, float centerX, float centerY, float innerRadius, float outerRadius)
    {
        float arcAngleSpan = endAngleDeg - startAngleDeg;
        int arcSegments = Math.Max(8, (int)(arcAngleSpan / 5));

        var pathBuilder = new PathBuilder();
        
        float startRad = startAngleDeg * MathF.PI / 180f;
        var innerStart = new SixLabors.ImageSharp.PointF(
            centerX + innerRadius * MathF.Cos(startRad),
            centerY + innerRadius * MathF.Sin(startRad)
        );
        pathBuilder.MoveTo(innerStart);
        
        var outerStart = new SixLabors.ImageSharp.PointF(
            centerX + outerRadius * MathF.Cos(startRad),
            centerY + outerRadius * MathF.Sin(startRad)
        );
        pathBuilder.LineTo(outerStart);
        
        for (int i = 1; i <= arcSegments; i++)
        {
            float t = i / (float)arcSegments;
            float angle = startAngleDeg + (arcAngleSpan * t);
            float rad = angle * MathF.PI / 180f;
            
            var arcPoint = new SixLabors.ImageSharp.PointF(
                centerX + outerRadius * MathF.Cos(rad),
                centerY + outerRadius * MathF.Sin(rad)
            );
            pathBuilder.LineTo(arcPoint);
        }
        
        float endRad = endAngleDeg * MathF.PI / 180f;
        var innerEnd = new SixLabors.ImageSharp.PointF(
            centerX + innerRadius * MathF.Cos(endRad),
            centerY + innerRadius * MathF.Sin(endRad)
        );
        pathBuilder.LineTo(innerEnd);
        
        for (int i = arcSegments - 1; i >= 0; i--)
        {
            float t = i / (float)arcSegments;
            float angle = startAngleDeg + (arcAngleSpan * t);
            float rad = angle * MathF.PI / 180f;
            
            var arcPoint = new SixLabors.ImageSharp.PointF(
                centerX + innerRadius * MathF.Cos(rad),
                centerY + innerRadius * MathF.Sin(rad)
            );
            pathBuilder.LineTo(arcPoint);
        }
        
        pathBuilder.CloseFigure();
        return pathBuilder.Build();
    }

    /// <summary>
    /// Calculates shard center at the specified scale.
    /// </summary>
    private SixLabors.ImageSharp.PointF CalculateShardCenterScaled(float startAngleDeg, float endAngleDeg, float centerX, float centerY, float innerRadius, float outerRadius)
    {
        float midAngleDeg = (startAngleDeg + endAngleDeg) / 2f;
        float midRadius = (innerRadius + outerRadius) / 2f;
        float midRad = midAngleDeg * MathF.PI / 180f;

        return new SixLabors.ImageSharp.PointF(
            centerX + midRadius * MathF.Cos(midRad),
            centerY + midRadius * MathF.Sin(midRad)
        );
    }

    /// <summary>
    /// Draws a shard onto the canvas at the specified scale.
    /// </summary>
    private void DrawShardOntoCanvasScaled(Image<Rgba32> canvas, Image<Rgba32> shardImage, IPath shardPath, SixLabors.ImageSharp.PointF shardCenter, int width, int height)
    {
        float offsetX = shardCenter.X - (width / 2f);
        float offsetY = shardCenter.Y - (height / 2f);

        using var shardLayer = shardImage.Clone();
        
        canvas.Mutate(ctx =>
        {
            ctx.SetGraphicsOptions(new GraphicsOptions { AlphaCompositionMode = PixelAlphaCompositionMode.SrcOver });
            ctx.Clip(shardPath, c =>
            {
                c.DrawImage(shardLayer, new SixLabors.ImageSharp.Point((int)offsetX, (int)offsetY), 1f);
            });
        });
    }

    /// <summary>
    /// Draws shard borders at the specified scale.
    /// </summary>
    private void DrawShardBordersScaled(Image<Rgba32> canvas, List<IPath> shardPaths, float centerX, float centerY, float innerRadius, float outerRadius, float borderThickness)
    {
        var pen = SixLabors.ImageSharp.Drawing.Processing.Pens.Solid(SixLabors.ImageSharp.Color.Black, borderThickness);
        
        canvas.Mutate(ctx =>
        {
            foreach (var path in shardPaths)
            {
                ctx.Draw(pen, path);
            }
            
            var innerCircle = new EllipsePolygon(centerX, centerY, innerRadius);
            ctx.Draw(pen, innerCircle);
            
            var outerCircle = new EllipsePolygon(centerX, centerY, outerRadius);
            ctx.Draw(pen, outerCircle);
        });
    }

    /// <summary>
    /// Draws the center circle with text at the specified scale.
    /// </summary>
    private void DrawCenterCircleScaled(Image<Rgba32> canvas, string centerText, float centerX, float centerY, float circleRadius, float borderThickness)
    {
        var centerCircle = new EllipsePolygon(centerX, centerY, circleRadius);
        
        canvas.Mutate(ctx =>
        {
            ctx.Fill(SixLabors.ImageSharp.Color.Black, centerCircle);
            var pen = SixLabors.ImageSharp.Drawing.Processing.Pens.Solid(SixLabors.ImageSharp.Color.White, Math.Max(1f, borderThickness * 0.75f));
            ctx.Draw(pen, centerCircle);
        });

        if (!string.IsNullOrWhiteSpace(centerText))
        {
            var text = centerText.Length > 3 ? centerText.Substring(0, 3) : centerText;
            
            SixLabors.Fonts.FontFamily fontFamily;
            try
            {
                // Try Noto Sans SC first (same font family as the rest of the project)
                fontFamily = SixLabors.Fonts.SystemFonts.Get("Noto Sans SC");
            }
            catch
            {
                try
                {
                    // Fallback to Segoe UI Symbol for unicode support
                    fontFamily = SixLabors.Fonts.SystemFonts.Get("Segoe UI Symbol");
                }
                catch
                {
                    // Last resort fallback
                    fontFamily = SixLabors.Fonts.SystemFonts.Get("Arial");
                }
            }
            
            float padding = 4f;
            float availableRadius = circleRadius - (borderThickness / 2f) - padding;
            float availableDiameter = availableRadius * 2f;
            
            float fontSize = 100f;
            const float minFontSize = 6f;
            SixLabors.Fonts.Font font;
            SixLabors.Fonts.FontRectangle textBounds;
            
            do
            {
                font = fontFamily.CreateFont(fontSize, SixLabors.Fonts.FontStyle.Bold);
                textBounds = SixLabors.Fonts.TextMeasurer.MeasureAdvance(text, new SixLabors.Fonts.TextOptions(font));
                
                float maxDimension = Math.Max(textBounds.Width, textBounds.Height);
                if (maxDimension <= availableDiameter) break;
                
                fontSize -= 1f;
            } while (fontSize >= minFontSize);
            
            var measureOptions = new SixLabors.Fonts.TextOptions(font) { Origin = System.Numerics.Vector2.Zero };
            var actualBounds = SixLabors.Fonts.TextMeasurer.MeasureBounds(text, measureOptions);
            
            float textCenterX = actualBounds.X + (actualBounds.Width / 2f);
            float textCenterY = actualBounds.Y + (actualBounds.Height / 2f);
            float originX = centerX - textCenterX;
            float originY = centerY - textCenterY;
            
            var textOptions = new RichTextOptions(font) { Origin = new System.Numerics.Vector2(originX, originY) };

            canvas.Mutate(ctx =>
            {
                ctx.DrawText(textOptions, text, SixLabors.ImageSharp.Color.White);
            });
        }
    }

    #region Background Generation

    // Background generation constants
    private const int BgWidth = 1920;
    private const int BgHeight = 1080;
    private const float BgCenterX = BgWidth / 2f;
    private const float BgCenterY = BgHeight / 2f;
    private const float InnerRadius = 80f;  // Small circle cut-off in center
    private const float OuterRadius = 1100f; // Large enough to cover corners

    // Border and center circle settings
    private const float BorderThickness = 4f;
    private const float CenterCircleRadius = 70f;

    /// <summary>
    /// Generates a composite background image from all map backgrounds.
    /// Each map's background is displayed as a shard arranged radially with rounded edges.
    /// Also generates highlight images for each shard for storyboard use.
    /// </summary>
    private async Task<HighlightInfo> GenerateMarathonBackgroundAsync(
        List<MarathonEntry> entries,
        MarathonMetadata metadata,
        string outputFolder,
        CancellationToken cancellationToken)
    {
        var highlightInfo = new HighlightInfo();
        
        // Filter to only map entries (skip pauses)
        var mapEntries = entries.Where(e => !e.IsPause && e.OsuFile != null).ToList();

        if (mapEntries.Count == 0)
        {
            Logger.Info("[Marathon] No map entries for background generation");
            return highlightInfo;
        }

        var outputPath = IOPath.Combine(outputFolder, "background.jpg");
        Logger.Info($"[Marathon] Generating composite background with {mapEntries.Count} shards");

        try
        {
            // Create the output canvas
            using var baseImage = new Image<Rgba32>(BgWidth, BgHeight);
            
            // Fill with black background
            baseImage.Mutate(ctx => ctx.Fill(SixLabors.ImageSharp.Color.Black));

            // Calculate angle per shard
            float anglePerShard = 360f / mapEntries.Count;
            // Offset by half a shard so the first map is CENTERED at top (12 o'clock), not starting there
            float startAngle = -90f - (anglePerShard / 2f);

            // Collect shard paths for border drawing later
            var shardPaths = new List<IPath>();
            var entryShardPaths = new List<(MarathonEntry Entry, IPath Path)>();

            for (int i = 0; i < mapEntries.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entry = mapEntries[i];
                float shardStartAngle = startAngle + (i * anglePerShard);
                float shardEndAngle = shardStartAngle + anglePerShard;

                Logger.Info($"[Marathon] Processing shard {i + 1}/{mapEntries.Count}: {entry.Title} ({shardStartAngle:F1} to {shardEndAngle:F1} degrees)");

                // Load the background image for this map
                using var shardImage = await LoadMapBackgroundAsync(entry, cancellationToken);

                // Build the shard path with rounded edges (both inner and outer)
                var shardPath = BuildShardPath(shardStartAngle, shardEndAngle);
                shardPaths.Add(shardPath);
                entryShardPaths.Add((entry, shardPath));

                // Calculate the center of the shard for image offset
                var shardCenter = CalculateShardCenter(shardStartAngle, shardEndAngle);

                // Draw the shard onto the output with proper centering
                DrawShardOntoCanvas(baseImage, shardImage, shardPath, shardCenter);
            }

            // Draw black borders on all shard edges
            DrawShardBorders(baseImage, shardPaths);

            // Draw center circle with text
            DrawCenterCircle(baseImage, metadata.CenterText);

            // Generate a random seed for glitch effects (so all images get identical glitch)
            int glitchSeed = new Random().Next();
            Logger.Info($"[Marathon] Using glitch seed: {glitchSeed}");

            // Generate highlighted versions for each map (with highlight baked in)
            // Each version has one shard highlighted, then ALL get the same glitch effect
            Logger.Info($"[Marathon] Generating {mapEntries.Count} highlighted versions...");
            
            var imagesToGlitch = new List<(Image<Rgba32> Image, string Path, Guid? EntryId)>();
            
            // Add the base background
            var baseClone = baseImage.Clone();
            imagesToGlitch.Add((baseClone, outputPath, null));
            
            // Create highlighted versions for each shard
            for (int i = 0; i < entryShardPaths.Count; i++)
            {
                var (entry, shardPath) = entryShardPaths[i];
                var highlightFilename = $"highlight_{i}.jpg";
                var highlightPath = IOPath.Combine(outputFolder, highlightFilename);
                
                // Clone the base image and add highlight to the specific shard
                var highlightedImage = baseImage.Clone();
                AddHighlightToShard(highlightedImage, shardPath);
                
                imagesToGlitch.Add((highlightedImage, highlightPath, entry.Id));
                highlightInfo.EntryToHighlightFile[entry.Id] = highlightFilename;
            }
            
            // Apply glitch effects to ALL images using the same seed
            if (metadata.GlitchIntensity > 0)
            {
                Logger.Info($"[Marathon] Applying glitch effects to {imagesToGlitch.Count} images...");
                foreach (var (image, path, _) in imagesToGlitch)
                {
                    ApplyGlitchEffects(image, metadata.GlitchIntensity, glitchSeed);
                }
            }
            
            // Save all images
            foreach (var (image, path, _) in imagesToGlitch)
            {
                await image.SaveAsJpegAsync(path, cancellationToken);
                image.Dispose();
            }
            
            Logger.Info($"[Marathon] All background images saved");
        }
        catch (Exception ex)
        {
            Logger.Info($"[Marathon] Background generation failed: {ex.Message}");
            // Don't fail the marathon creation if background generation fails
        }

        return highlightInfo;
    }

    /// <summary>
    /// Adds a white highlight overlay to a specific shard on the image.
    /// </summary>
    private void AddHighlightToShard(Image<Rgba32> image, IPath shardPath)
    {
        // Draw white semi-transparent fill for the shard
        var highlightColor = new Rgba32(255, 255, 255, 80); // White with ~30% opacity
        image.Mutate(ctx => ctx.Fill(highlightColor, shardPath));
        
        // Add a white border for extra visibility
        var borderPen = SixLabors.ImageSharp.Drawing.Processing.Pens.Solid(
            new Rgba32(255, 255, 255, 200), 4f);
        image.Mutate(ctx => ctx.Draw(borderPen, shardPath));
    }

    /// <summary>
    /// Injects storyboard events directly into the .osu file's Events section.
    /// This ensures osu! recognizes the storyboard without needing a separate .osb file.
    /// </summary>
    private void InjectStoryboardIntoOsuFile(
        string osuFilePath,
        List<ProcessedMapSegment> segments,
        HighlightInfo highlightInfo)
    {
        if (highlightInfo.EntryToHighlightFile.Count == 0)
        {
            Logger.Info("[Marathon] No highlight info available, skipping storyboard injection");
            return;
        }

        // Read the existing .osu file
        var lines = File.ReadAllLines(osuFilePath).ToList();
        
        // Find the storyboard layer comments and inject our sprites
        int insertIndex = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Contains("//Storyboard Layer 0 (Background)"))
            {
                insertIndex = i + 1;
                break;
            }
        }

        if (insertIndex == -1)
        {
            Logger.Info("[Marathon] Could not find storyboard section in .osu file");
            return;
        }

        // Build storyboard sprite commands
        var storyboardLines = new List<string>();
        
        // Fade duration for smooth transitions (in ms)
        const int fadeDuration = 200;

        // Process each segment and create sprite swap commands
        foreach (var segment in segments)
        {
            // Skip pause entries
            if (segment.Entry.IsPause)
                continue;

            // Check if we have a highlight for this entry
            if (!highlightInfo.EntryToHighlightFile.TryGetValue(segment.Entry.Id, out var highlightFilename))
                continue;

            // Calculate timing
            var startTime = (int)segment.OffsetInMarathon;
            var endTime = (int)(segment.OffsetInMarathon + segment.Duration);
            
            // Fade in starts slightly before the section, fade out ends slightly after
            var fadeInStart = Math.Max(0, startTime - fadeDuration);
            var fadeOutEnd = endTime + fadeDuration;

            // Sprite declaration for full background image
            // Layer: Background (behind gameplay elements)
            // Origin: Centre
            // Position: Center of osu! playfield (320, 240)
            storyboardLines.Add($"Sprite,Background,Centre,\"{highlightFilename}\",320,240");
            
            // Scale command to fill the screen
            // osu! storyboard uses 640x480 as base, our images are 1920x1080
            // Scale of ~0.45 will make the image cover the playfield area
            storyboardLines.Add($" S,0,0,,0.45");
            
            // Fade commands: F,easing,starttime,endtime,startopacity,endopacity
            // Fade in
            storyboardLines.Add($" F,0,{fadeInStart},{startTime},0,1");
            // Stay visible
            storyboardLines.Add($" F,0,{startTime},{endTime},1");
            // Fade out
            storyboardLines.Add($" F,0,{endTime},{fadeOutEnd},1,0");
        }

        // Insert storyboard lines into the file
        lines.InsertRange(insertIndex, storyboardLines);

        // Write back the modified file
        File.WriteAllLines(osuFilePath, lines);
        Logger.Info($"[Marathon] Storyboard injected into .osu file ({storyboardLines.Count} lines)");
    }

    /// <summary>
    /// Generates a text file documenting the marathon structure with all maps and breaks.
    /// </summary>
    private void GenerateStructureDocument(
        List<ProcessedMapSegment> segments,
        MarathonMetadata metadata,
        string outputFolder)
    {
        var docPath = IOPath.Combine(outputFolder, "marathon_structure.txt");
        var sb = new StringBuilder();

        // Header
        sb.AppendLine("================================================================================");
        sb.AppendLine("                           MARATHON STRUCTURE");
        sb.AppendLine("================================================================================");
        sb.AppendLine();
        sb.AppendLine($"Title:    {metadata.Artist} - {metadata.Title}");
        sb.AppendLine($"Creator:  {metadata.Creator}");
        sb.AppendLine($"Version:  {metadata.Version}");
        sb.AppendLine();

        // Calculate totals
        var totalDuration = segments.Sum(s => s.Duration);
        var mapCount = segments.Count(s => !s.Entry.IsPause);
        var breakCount = segments.Count(s => s.Entry.IsPause);
        var totalBreakTime = segments.Where(s => s.Entry.IsPause).Sum(s => s.Duration);

        sb.AppendLine($"Total Maps:       {mapCount}");
        sb.AppendLine($"Total Breaks:     {breakCount}");
        sb.AppendLine($"Total Duration:   {FormatDuration(totalDuration)}");
        sb.AppendLine($"Total Break Time: {FormatDuration(totalBreakTime)}");
        sb.AppendLine();
        sb.AppendLine("================================================================================");
        sb.AppendLine("                              TRACK LIST");
        sb.AppendLine("================================================================================");
        sb.AppendLine();

        // Table header
        sb.AppendLine(new string('-', 120));
        sb.AppendLine($"{"#",-4} {"Type",-8} {"Start",-10} {"Duration",-10} {"Rate",-6} {"Title",-40} {"Difficulty",-20}");
        sb.AppendLine(new string('-', 120));

        int trackNum = 1;
        foreach (var segment in segments)
        {
            var startTime = FormatDuration(segment.OffsetInMarathon);
            var duration = FormatDuration(segment.Duration);

            if (segment.Entry.IsPause)
            {
                sb.AppendLine($"{trackNum,-4} {"BREAK",-8} {startTime,-10} {duration,-10} {"-",-6} {"--- Break ---",-40} {"-",-20}");
            }
            else
            {
                var entry = segment.Entry;
                var rate = entry.Rate.ToString("0.00") + "x";
                var title = TruncateString(entry.Title, 38);
                var difficulty = TruncateString(entry.Version, 18);

                sb.AppendLine($"{trackNum,-4} {"MAP",-8} {startTime,-10} {duration,-10} {rate,-6} {title,-40} {difficulty,-20}");
            }
            trackNum++;
        }

        sb.AppendLine(new string('-', 120));
        sb.AppendLine();
        sb.AppendLine("================================================================================");
        sb.AppendLine("                           DETAILED MAP INFO");
        sb.AppendLine("================================================================================");
        sb.AppendLine();

        trackNum = 1;
        foreach (var segment in segments)
        {
            if (segment.Entry.IsPause)
            {
                sb.AppendLine($"[{trackNum}] BREAK");
                sb.AppendLine($"    Duration: {FormatDuration(segment.Duration)} ({segment.Duration:F0}ms)");
                sb.AppendLine();
            }
            else
            {
                var entry = segment.Entry;
                var osuFile = entry.OsuFile;

                sb.AppendLine($"[{trackNum}] {entry.Title}");
                sb.AppendLine($"    Artist:     {osuFile?.Artist ?? "Unknown"}");
                sb.AppendLine($"    Creator:    {entry.Creator}");
                sb.AppendLine($"    Difficulty: {entry.Version}");
                sb.AppendLine($"    Rate:       {entry.Rate:0.00}x");
                sb.AppendLine($"    BPM:        {entry.Bpm:F1} (original: {entry.DominantBpm:F1})");
                sb.AppendLine($"    Duration:   {FormatDuration(segment.Duration)} ({segment.Duration:F0}ms)");
                sb.AppendLine($"    Start:      {FormatDuration(segment.OffsetInMarathon)}");
                
                // Generate relative path from Songs folder
                if (osuFile?.FilePath != null)
                {
                    var relativePath = GetRelativePathFromSongs(osuFile.FilePath);
                    sb.AppendLine($"    Path:       {relativePath}");
                }
                
                // MSD info if available
                if (entry.MsdValues != null)
                {
                    var msd = entry.MsdValues;
                    sb.AppendLine($"    MSD:        Overall={msd.Overall:F1}, Stream={msd.Stream:F1}, JS={msd.Jumpstream:F1}, HS={msd.Handstream:F1}");
                    sb.AppendLine($"                Stamina={msd.Stamina:F1}, Jack={msd.Jackspeed:F1}, CJ={msd.Chordjack:F1}, Tech={msd.Technical:F1}");
                }
                
                sb.AppendLine();
            }
            trackNum++;
        }

        sb.AppendLine("================================================================================");
        sb.AppendLine($"Generated by Companella Marathon Creator");
        sb.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("================================================================================");

        File.WriteAllText(docPath, sb.ToString());
        Logger.Info($"[Marathon] Structure document saved: {docPath}");
    }

    /// <summary>
    /// Formats a duration in milliseconds to a readable string (mm:ss or hh:mm:ss).
    /// </summary>
    private static string FormatDuration(double milliseconds)
    {
        var timeSpan = TimeSpan.FromMilliseconds(milliseconds);
        if (timeSpan.TotalHours >= 1)
        {
            return $"{(int)timeSpan.TotalHours}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
        }
        return $"{(int)timeSpan.TotalMinutes}:{timeSpan.Seconds:D2}";
    }

    /// <summary>
    /// Truncates a string to the specified length, adding "..." if truncated.
    /// </summary>
    private static string TruncateString(string str, int maxLength)
    {
        if (string.IsNullOrEmpty(str)) return str;
        if (str.Length <= maxLength) return str;
        return str.Substring(0, maxLength - 3) + "...";
    }

    /// <summary>
    /// Gets a relative path from the osu! Songs folder.
    /// </summary>
    private static string GetRelativePathFromSongs(string filePath)
    {
        // Try to find "Songs" in the path and return everything after it
        var normalizedPath = filePath.Replace('/', '\\');
        var songsIndex = normalizedPath.IndexOf("\\Songs\\", StringComparison.OrdinalIgnoreCase);
        if (songsIndex >= 0)
        {
            return normalizedPath.Substring(songsIndex + 7); // +7 to skip "\Songs\"
        }
        
        // Fallback: just return the filename and parent folder
        var directory = IOPath.GetDirectoryName(filePath);
        var folderName = IOPath.GetFileName(directory);
        var fileName = IOPath.GetFileName(filePath);
        return IOPath.Combine(folderName ?? "", fileName);
    }

    /// <summary>
    /// Loads a map's background image, or creates a black placeholder if not available.
    /// </summary>
    private async Task<Image<Rgba32>> LoadMapBackgroundAsync(MarathonEntry entry, CancellationToken cancellationToken)
    {
        if (entry.OsuFile?.BackgroundFilename != null)
        {
            var bgPath = IOPath.Combine(entry.OsuFile.DirectoryPath, entry.OsuFile.BackgroundFilename);
            if (File.Exists(bgPath))
            {
                try
                {
                    using var stream = File.OpenRead(bgPath);
                    var image = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(stream, cancellationToken);
                    
                    // Resize to match output dimensions for consistent sampling
                    image.Mutate(ctx => ctx.Resize(new ResizeOptions
                    {
                        Size = new SixLabors.ImageSharp.Size(BgWidth, BgHeight),
                        Mode = ResizeMode.Crop,
                        Position = AnchorPositionMode.Center
                    }));
                    
                    return image;
                }
                catch (Exception ex)
                {
                    Logger.Info($"[Marathon] Failed to load background for {entry.Title}: {ex.Message}");
                }
            }
        }

        // Create black placeholder
        var placeholder = new Image<Rgba32>(BgWidth, BgHeight);
        placeholder.Mutate(ctx => ctx.Fill(SixLabors.ImageSharp.Color.Black));
        return placeholder;
    }

    /// <summary>
    /// Builds a shard path with rounded edges (arcs for both inner and outer edges).
    /// </summary>
    private IPath BuildShardPath(float startAngleDeg, float endAngleDeg)
    {
        float arcAngleSpan = endAngleDeg - startAngleDeg;
        int arcSegments = Math.Max(12, (int)(arcAngleSpan / 3)); // More segments for smoother curves

        var pathBuilder = new PathBuilder();
        
        // Start at inner arc start point
        float startRad = startAngleDeg * MathF.PI / 180f;
        var innerStart = new SixLabors.ImageSharp.PointF(
            BgCenterX + InnerRadius * MathF.Cos(startRad),
            BgCenterY + InnerRadius * MathF.Sin(startRad)
        );
        pathBuilder.MoveTo(innerStart);
        
        // Line from inner start to outer start (radial edge)
        var outerStart = new SixLabors.ImageSharp.PointF(
            BgCenterX + OuterRadius * MathF.Cos(startRad),
            BgCenterY + OuterRadius * MathF.Sin(startRad)
        );
        pathBuilder.LineTo(outerStart);
        
        // Arc along outer edge (clockwise from start to end angle)
        for (int i = 1; i <= arcSegments; i++)
        {
            float t = i / (float)arcSegments;
            float angle = startAngleDeg + (arcAngleSpan * t);
            float rad = angle * MathF.PI / 180f;
            
            var arcPoint = new SixLabors.ImageSharp.PointF(
                BgCenterX + OuterRadius * MathF.Cos(rad),
                BgCenterY + OuterRadius * MathF.Sin(rad)
            );
            pathBuilder.LineTo(arcPoint);
        }
        
        // Line from outer end to inner end (radial edge)
        float endRad = endAngleDeg * MathF.PI / 180f;
        var innerEnd = new SixLabors.ImageSharp.PointF(
            BgCenterX + InnerRadius * MathF.Cos(endRad),
            BgCenterY + InnerRadius * MathF.Sin(endRad)
        );
        pathBuilder.LineTo(innerEnd);
        
        // Arc along inner edge (counter-clockwise from end back to start angle)
        for (int i = arcSegments - 1; i >= 0; i--)
        {
            float t = i / (float)arcSegments;
            float angle = startAngleDeg + (arcAngleSpan * t);
            float rad = angle * MathF.PI / 180f;
            
            var arcPoint = new SixLabors.ImageSharp.PointF(
                BgCenterX + InnerRadius * MathF.Cos(rad),
                BgCenterY + InnerRadius * MathF.Sin(rad)
            );
            pathBuilder.LineTo(arcPoint);
        }
        
        // Close the path
        pathBuilder.CloseFigure();
        
        return pathBuilder.Build();
    }

    /// <summary>
    /// Calculates the center point of a shard (for image offset calculation).
    /// </summary>
    private SixLabors.ImageSharp.PointF CalculateShardCenter(float startAngleDeg, float endAngleDeg)
    {
        // The center of the shard is at the middle angle and middle radius
        float midAngleDeg = (startAngleDeg + endAngleDeg) / 2f;
        float midRadius = (InnerRadius + OuterRadius) / 2f;
        float midRad = midAngleDeg * MathF.PI / 180f;

        return new SixLabors.ImageSharp.PointF(
            BgCenterX + midRadius * MathF.Cos(midRad),
            BgCenterY + midRadius * MathF.Sin(midRad)
        );
    }

    /// <summary>
    /// Draws a background image shard onto the output canvas, clipped to the shard shape.
    /// The source image is offset so its center aligns with the shard's center.
    /// </summary>
    private void DrawShardOntoCanvas(Image<Rgba32> canvas, Image<Rgba32> shardImage, IPath shardPath, SixLabors.ImageSharp.PointF shardCenter)
    {
        // Calculate offset to center the source image on the shard
        // Source image center is at (BgWidth/2, BgHeight/2)
        // We want that center to appear at shardCenter
        float offsetX = shardCenter.X - (BgWidth / 2f);
        float offsetY = shardCenter.Y - (BgHeight / 2f);

        // Create a temporary image with just this shard
        using var shardLayer = shardImage.Clone();
        
        // Clear everything outside the shard by drawing with a clip
        canvas.Mutate(ctx =>
        {
            // Use SetGraphicsOptions to enable clipping
            ctx.SetGraphicsOptions(new GraphicsOptions { AlphaCompositionMode = PixelAlphaCompositionMode.SrcOver });
            
            // Fill the shard region with the background image, offset to center it
            ctx.Clip(shardPath, c =>
            {
                c.DrawImage(shardLayer, new SixLabors.ImageSharp.Point((int)offsetX, (int)offsetY), 1f);
            });
        });
    }

    /// <summary>
    /// Draws black border strokes on all shard edges.
    /// </summary>
    private void DrawShardBorders(Image<Rgba32> canvas, List<IPath> shardPaths)
    {
        var pen = SixLabors.ImageSharp.Drawing.Processing.Pens.Solid(SixLabors.ImageSharp.Color.Black, BorderThickness);
        
        canvas.Mutate(ctx =>
        {
            foreach (var path in shardPaths)
            {
                ctx.Draw(pen, path);
            }
            
            // Also draw the inner circle border
            var innerCircle = new EllipsePolygon(BgCenterX, BgCenterY, InnerRadius);
            ctx.Draw(pen, innerCircle);
            
            // And the outer circle border
            var outerCircle = new EllipsePolygon(BgCenterX, BgCenterY, OuterRadius);
            ctx.Draw(pen, outerCircle);
        });
    }

    /// <summary>
    /// Draws the center circle with optional text/symbols.
    /// </summary>
    private void DrawCenterCircle(Image<Rgba32> canvas, string centerText)
    {
        // Draw filled black circle in center
        var centerCircle = new EllipsePolygon(BgCenterX, BgCenterY, CenterCircleRadius);
        
        const float borderWidth = 3f;
        
        canvas.Mutate(ctx =>
        {
            ctx.Fill(SixLabors.ImageSharp.Color.Black, centerCircle);
            
            // Draw white border around center circle
            var pen = SixLabors.ImageSharp.Drawing.Processing.Pens.Solid(SixLabors.ImageSharp.Color.White, borderWidth);
            ctx.Draw(pen, centerCircle);
        });

        // Draw center text if provided
        if (!string.IsNullOrWhiteSpace(centerText))
        {
            // Limit to 3 characters
            var text = centerText.Length > 3 ? centerText.Substring(0, 3) : centerText;
            
            // Use Noto Sans SC for consistency with the rest of the project
            SixLabors.Fonts.FontFamily fontFamily;
            try
            {
                // Try Noto Sans SC first (same font family as the rest of the project)
                fontFamily = SixLabors.Fonts.SystemFonts.Get("Noto Sans SC");
            }
            catch
            {
                try
                {
                    // Fallback to Segoe UI Symbol for unicode support
                    fontFamily = SixLabors.Fonts.SystemFonts.Get("Segoe UI Symbol");
                }
                catch
                {
                    // Last resort fallback
                    fontFamily = SixLabors.Fonts.SystemFonts.Get("Arial");
                }
            }
            
            // Calculate available space inside the circle (accounting for border and padding)
            const float padding = 8f;
            float availableRadius = CenterCircleRadius - (borderWidth / 2f) - padding;
            float availableDiameter = availableRadius * 2f;
            
            // Auto-size the font to fit within the circle
            float fontSize = 200f; // Start large
            const float minFontSize = 10f;
            SixLabors.Fonts.Font font;
            SixLabors.Fonts.FontRectangle textBounds;
            
            do
            {
                font = fontFamily.CreateFont(fontSize, SixLabors.Fonts.FontStyle.Bold);
                textBounds = SixLabors.Fonts.TextMeasurer.MeasureAdvance(text, new SixLabors.Fonts.TextOptions(font));
                
                // Check if text fits within the available circle diameter
                // Use the larger of width/height to ensure it fits
                float maxDimension = Math.Max(textBounds.Width, textBounds.Height);
                
                if (maxDimension <= availableDiameter)
                {
                    break;
                }
                
                fontSize -= 2f;
            } while (fontSize >= minFontSize);
            
            // Measure the actual rendered bounds for precise centering
            var measureOptions = new SixLabors.Fonts.TextOptions(font)
            {
                Origin = System.Numerics.Vector2.Zero
            };
            var actualBounds = SixLabors.Fonts.TextMeasurer.MeasureBounds(text, measureOptions);
            
            // Calculate the center position based on actual rendered bounds
            // This accounts for font metrics like ascenders/descenders
            float textCenterX = actualBounds.X + (actualBounds.Width / 2f);
            float textCenterY = actualBounds.Y + (actualBounds.Height / 2f);
            
            // Position the text so its visual center aligns with the circle center
            float originX = BgCenterX - textCenterX;
            float originY = BgCenterY - textCenterY;
            
            // Use RichTextOptions for DrawText with manual positioning
            var textOptions = new RichTextOptions(font)
            {
                Origin = new System.Numerics.Vector2(originX, originY)
            };

            canvas.Mutate(ctx =>
            {
                ctx.DrawText(textOptions, text, SixLabors.ImageSharp.Color.White);
            });
        }
    }

    #endregion

    #region Glitch Effects

    /// <summary>
    /// Applies glitch effects to the image based on intensity using a seeded random.
    /// Using the same seed produces identical glitch patterns.
    /// Effects include: RGB shift, scanlines, image distortion, and block glitches.
    /// </summary>
    private void ApplyGlitchEffects(Image<Rgba32> image, float intensity, int seed)
    {
        Logger.Info($"[Marathon] Applying glitch effects at {intensity:P0} intensity (seed: {seed})");
        
        // Create a seeded random for reproducible effects
        var random = new Random(seed);
        
        // Apply effects in order with intensity scaling
        ApplyRgbShift(image, intensity, random);
        ApplyScanlines(image, intensity);
        ApplyImageDistortion(image, intensity, random);
        ApplyBlockGlitches(image, intensity, random);
    }

    /// <summary>
    /// Applies chromatic aberration / RGB channel separation effect.
    /// </summary>
    private void ApplyRgbShift(Image<Rgba32> image, float intensity, Random random)
    {
        // Max shift scales with intensity (0-30 pixels at max)
        int maxShift = (int)(30 * intensity);
        if (maxShift < 1) return;

        // Create copies for each channel
        using var redChannel = image.Clone();
        using var blueChannel = image.Clone();

        // Random shift directions
        int redShiftX = random.Next(-maxShift, maxShift + 1);
        int redShiftY = random.Next(-maxShift / 3, maxShift / 3 + 1);
        int blueShiftX = random.Next(-maxShift, maxShift + 1);
        int blueShiftY = random.Next(-maxShift / 3, maxShift / 3 + 1);

        // Process each pixel
        image.ProcessPixelRows(redChannel, blueChannel, (mainAccessor, redAccessor, blueAccessor) =>
        {
            for (int y = 0; y < mainAccessor.Height; y++)
            {
                var mainRow = mainAccessor.GetRowSpan(y);
                
                for (int x = 0; x < mainAccessor.Width; x++)
                {
                    ref var pixel = ref mainRow[x];
                    
                    // Get shifted red channel
                    int redY = Math.Clamp(y + redShiftY, 0, mainAccessor.Height - 1);
                    int redX = Math.Clamp(x + redShiftX, 0, mainAccessor.Width - 1);
                    var redRow = redAccessor.GetRowSpan(redY);
                    byte newRed = redRow[redX].R;
                    
                    // Get shifted blue channel
                    int blueY = Math.Clamp(y + blueShiftY, 0, mainAccessor.Height - 1);
                    int blueX = Math.Clamp(x + blueShiftX, 0, mainAccessor.Width - 1);
                    var blueRow = blueAccessor.GetRowSpan(blueY);
                    byte newBlue = blueRow[blueX].B;
                    
                    // Keep original green, shift red and blue
                    pixel = new Rgba32(newRed, pixel.G, newBlue, pixel.A);
                }
            }
        });
    }

    /// <summary>
    /// Applies horizontal scanline effect.
    /// </summary>
    private void ApplyScanlines(Image<Rgba32> image, float intensity)
    {
        // Scanline frequency and darkness scale with intensity
        int scanlineSpacing = Math.Max(2, 6 - (int)(4 * intensity)); // 6 to 2 pixel spacing
        float darkness = 0.3f + (0.5f * intensity); // 30% to 80% dark

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                // Apply scanline pattern
                if (y % scanlineSpacing == 0)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < accessor.Width; x++)
                    {
                        ref var pixel = ref row[x];
                        pixel = new Rgba32(
                            (byte)(pixel.R * (1 - darkness)),
                            (byte)(pixel.G * (1 - darkness)),
                            (byte)(pixel.B * (1 - darkness)),
                            pixel.A
                        );
                    }
                }
            }
        });
    }

    /// <summary>
    /// Applies horizontal wave distortion effect.
    /// </summary>
    private void ApplyImageDistortion(Image<Rgba32> image, float intensity, Random random)
    {
        // Distortion amplitude scales with intensity (0-40 pixels at max)
        float amplitude = 40 * intensity;
        if (amplitude < 1) return;

        // Random wave frequency
        float frequency = 0.005f + ((float)random.NextDouble() * 0.015f);
        float phase = (float)random.NextDouble() * MathF.PI * 2;

        using var original = image.Clone();

        image.ProcessPixelRows(original, (destAccessor, srcAccessor) =>
        {
            for (int y = 0; y < destAccessor.Height; y++)
            {
                var destRow = destAccessor.GetRowSpan(y);
                
                // Calculate wave offset for this row
                int offset = (int)(MathF.Sin(y * frequency + phase) * amplitude);
                
                for (int x = 0; x < destAccessor.Width; x++)
                {
                    int srcX = x + offset;
                    
                    // Wrap around or clamp
                    if (srcX < 0) srcX = 0;
                    if (srcX >= srcAccessor.Width) srcX = srcAccessor.Width - 1;
                    
                    var srcRow = srcAccessor.GetRowSpan(y);
                    destRow[x] = srcRow[srcX];
                }
            }
        });
    }

    /// <summary>
    /// Applies random block glitch effect (displaced rectangular regions).
    /// </summary>
    private void ApplyBlockGlitches(Image<Rgba32> image, float intensity, Random random)
    {
        // Number of glitch blocks scales with intensity
        int blockCount = (int)(20 * intensity);
        if (blockCount < 1) return;

        using var original = image.Clone();

        for (int i = 0; i < blockCount; i++)
        {
            // Random block dimensions
            int blockWidth = random.Next(50, 400);
            int blockHeight = random.Next(5, 50);
            
            // Random source position
            int srcX = random.Next(0, image.Width - blockWidth);
            int srcY = random.Next(0, image.Height - blockHeight);
            
            // Random horizontal displacement
            int offsetX = random.Next(-100, 101);
            int destX = Math.Clamp(srcX + offsetX, 0, image.Width - blockWidth);
            
            // Copy the block with displacement
            image.ProcessPixelRows(original, (destAccessor, srcAccessor) =>
            {
                for (int y = 0; y < blockHeight && (srcY + y) < srcAccessor.Height; y++)
                {
                    var srcRow = srcAccessor.GetRowSpan(srcY + y);
                    var destRow = destAccessor.GetRowSpan(srcY + y);
                    
                    for (int x = 0; x < blockWidth && (srcX + x) < srcAccessor.Width && (destX + x) < destAccessor.Width; x++)
                    {
                        if (destX + x >= 0)
                        {
                            destRow[destX + x] = srcRow[srcX + x];
                        }
                    }
                }
            });
            
            // Occasionally add color tint to block
            if (random.NextDouble() < 0.3)
            {
                byte tintR = (byte)(random.NextDouble() < 0.5 ? 255 : 0);
                byte tintG = (byte)(random.NextDouble() < 0.5 ? 255 : 0);
                byte tintB = (byte)(random.NextDouble() < 0.5 ? 255 : 0);
                float tintStrength = 0.2f + (0.3f * intensity);
                
                image.ProcessPixelRows(accessor =>
                {
                    for (int y = srcY; y < srcY + blockHeight && y < accessor.Height; y++)
                    {
                        var row = accessor.GetRowSpan(y);
                        for (int x = destX; x < destX + blockWidth && x < accessor.Width; x++)
                        {
                            if (x >= 0)
                            {
                                ref var pixel = ref row[x];
                                pixel = new Rgba32(
                                    (byte)(pixel.R * (1 - tintStrength) + tintR * tintStrength),
                                    (byte)(pixel.G * (1 - tintStrength) + tintG * tintStrength),
                                    (byte)(pixel.B * (1 - tintStrength) + tintB * tintStrength),
                                    pixel.A
                                );
                            }
                        }
                    }
                });
            }
        }
    }

    /// <summary>
    /// Applies glitch effects scaled for preview resolution.
    /// All pixel-based values are scaled down proportionally.
    /// </summary>
    private void ApplyGlitchEffectsScaled(Image<Rgba32> image, float intensity, int seed, float scale)
    {
        var random = new Random(seed);
        
        ApplyRgbShiftScaled(image, intensity, random, scale);
        ApplyScanlinesScaled(image, intensity, scale);
        ApplyImageDistortionScaled(image, intensity, random, scale);
        ApplyBlockGlitchesScaled(image, intensity, random, scale);
    }

    /// <summary>
    /// Applies RGB shift effect scaled for preview resolution.
    /// </summary>
    private void ApplyRgbShiftScaled(Image<Rgba32> image, float intensity, Random random, float scale)
    {
        // Scale the max shift (original: 30 pixels at 1920px)
        int maxShift = (int)(30 * intensity * scale);
        if (maxShift < 1) return;

        using var redChannel = image.Clone();
        using var blueChannel = image.Clone();

        int redShiftX = random.Next(-maxShift, maxShift + 1);
        int redShiftY = random.Next(-maxShift / 3, maxShift / 3 + 1);
        int blueShiftX = random.Next(-maxShift, maxShift + 1);
        int blueShiftY = random.Next(-maxShift / 3, maxShift / 3 + 1);

        image.ProcessPixelRows(redChannel, blueChannel, (mainAccessor, redAccessor, blueAccessor) =>
        {
            for (int y = 0; y < mainAccessor.Height; y++)
            {
                var mainRow = mainAccessor.GetRowSpan(y);
                
                for (int x = 0; x < mainAccessor.Width; x++)
                {
                    ref var pixel = ref mainRow[x];
                    
                    int redY = Math.Clamp(y + redShiftY, 0, mainAccessor.Height - 1);
                    int redX = Math.Clamp(x + redShiftX, 0, mainAccessor.Width - 1);
                    var redRow = redAccessor.GetRowSpan(redY);
                    byte newRed = redRow[redX].R;
                    
                    int blueY = Math.Clamp(y + blueShiftY, 0, mainAccessor.Height - 1);
                    int blueX = Math.Clamp(x + blueShiftX, 0, mainAccessor.Width - 1);
                    var blueRow = blueAccessor.GetRowSpan(blueY);
                    byte newBlue = blueRow[blueX].B;
                    
                    pixel = new Rgba32(newRed, pixel.G, newBlue, pixel.A);
                }
            }
        });
    }

    /// <summary>
    /// Applies scanline effect scaled for preview resolution.
    /// </summary>
    private void ApplyScanlinesScaled(Image<Rgba32> image, float intensity, float scale)
    {
        // Scale scanline spacing (original: 6 to 2 pixels at 1920px)
        int baseSpacing = Math.Max(2, 6 - (int)(4 * intensity));
        int scanlineSpacing = Math.Max(1, (int)(baseSpacing * scale));
        float darkness = 0.3f + (0.5f * intensity);

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                if (y % scanlineSpacing == 0)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < accessor.Width; x++)
                    {
                        ref var pixel = ref row[x];
                        pixel = new Rgba32(
                            (byte)(pixel.R * (1 - darkness)),
                            (byte)(pixel.G * (1 - darkness)),
                            (byte)(pixel.B * (1 - darkness)),
                            pixel.A
                        );
                    }
                }
            }
        });
    }

    /// <summary>
    /// Applies wave distortion effect scaled for preview resolution.
    /// </summary>
    private void ApplyImageDistortionScaled(Image<Rgba32> image, float intensity, Random random, float scale)
    {
        // Scale amplitude (original: 40 pixels at 1920px)
        float amplitude = 40 * intensity * scale;
        if (amplitude < 1) return;

        // Frequency needs to be scaled inversely to work with smaller image
        float frequency = (0.005f + ((float)random.NextDouble() * 0.015f)) / scale;
        float phase = (float)random.NextDouble() * MathF.PI * 2;

        using var original = image.Clone();

        image.ProcessPixelRows(original, (destAccessor, srcAccessor) =>
        {
            for (int y = 0; y < destAccessor.Height; y++)
            {
                var destRow = destAccessor.GetRowSpan(y);
                int offset = (int)(MathF.Sin(y * frequency + phase) * amplitude);
                
                for (int x = 0; x < destAccessor.Width; x++)
                {
                    int srcX = x + offset;
                    if (srcX < 0) srcX = 0;
                    if (srcX >= srcAccessor.Width) srcX = srcAccessor.Width - 1;
                    
                    var srcRow = srcAccessor.GetRowSpan(y);
                    destRow[x] = srcRow[srcX];
                }
            }
        });
    }

    /// <summary>
    /// Applies block glitch effect scaled for preview resolution.
    /// </summary>
    private void ApplyBlockGlitchesScaled(Image<Rgba32> image, float intensity, Random random, float scale)
    {
        // Scale block count (keep similar visual density)
        int blockCount = (int)(20 * intensity);
        if (blockCount < 1) return;

        using var original = image.Clone();

        for (int i = 0; i < blockCount; i++)
        {
            // Scale block dimensions (original: 50-400 width, 5-50 height at 1920px)
            int blockWidth = (int)(random.Next(50, 400) * scale);
            int blockHeight = (int)(random.Next(5, 50) * scale);
            
            // Ensure minimum size
            blockWidth = Math.Max(5, blockWidth);
            blockHeight = Math.Max(2, blockHeight);
            
            // Ensure blocks fit in image
            if (blockWidth >= image.Width) blockWidth = image.Width / 2;
            if (blockHeight >= image.Height) blockHeight = image.Height / 4;
            
            int srcX = random.Next(0, Math.Max(1, image.Width - blockWidth));
            int srcY = random.Next(0, Math.Max(1, image.Height - blockHeight));
            
            // Scale displacement (original: -100 to 100 at 1920px)
            int maxOffset = (int)(100 * scale);
            int offsetX = random.Next(-maxOffset, maxOffset + 1);
            int destX = Math.Clamp(srcX + offsetX, 0, Math.Max(0, image.Width - blockWidth));
            
            image.ProcessPixelRows(original, (destAccessor, srcAccessor) =>
            {
                for (int y = 0; y < blockHeight && (srcY + y) < srcAccessor.Height; y++)
                {
                    var srcRow = srcAccessor.GetRowSpan(srcY + y);
                    var destRow = destAccessor.GetRowSpan(srcY + y);
                    
                    for (int x = 0; x < blockWidth && (srcX + x) < srcAccessor.Width && (destX + x) < destAccessor.Width; x++)
                    {
                        if (destX + x >= 0)
                        {
                            destRow[destX + x] = srcRow[srcX + x];
                        }
                    }
                }
            });
            
            if (random.NextDouble() < 0.3)
            {
                byte tintR = (byte)(random.NextDouble() < 0.5 ? 255 : 0);
                byte tintG = (byte)(random.NextDouble() < 0.5 ? 255 : 0);
                byte tintB = (byte)(random.NextDouble() < 0.5 ? 255 : 0);
                float tintStrength = 0.2f + (0.3f * intensity);
                
                image.ProcessPixelRows(accessor =>
                {
                    for (int y = srcY; y < srcY + blockHeight && y < accessor.Height; y++)
                    {
                        var row = accessor.GetRowSpan(y);
                        for (int x = destX; x < destX + blockWidth && x < accessor.Width; x++)
                        {
                            if (x >= 0)
                            {
                                ref var pixel = ref row[x];
                                pixel = new Rgba32(
                                    (byte)(pixel.R * (1 - tintStrength) + tintR * tintStrength),
                                    (byte)(pixel.G * (1 - tintStrength) + tintG * tintStrength),
                                    (byte)(pixel.B * (1 - tintStrength) + tintB * tintStrength),
                                    pixel.A
                                );
                            }
                        }
                    }
                });
            }
        }
    }

    #endregion
}

