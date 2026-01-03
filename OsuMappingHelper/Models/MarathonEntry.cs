using System.Security.Cryptography;

namespace OsuMappingHelper.Models;

/// <summary>
/// Represents a single beatmap entry in the marathon creator list.
/// Can also represent a pause/break section.
/// </summary>
public class MarathonEntry
{
    /// <summary>
    /// Whether this entry is a pause/break section (no notes, silent audio).
    /// </summary>
    public bool IsPause { get; set; }

    /// <summary>
    /// Duration of the pause in seconds (only used when IsPause is true).
    /// </summary>
    public double PauseDurationSeconds { get; set; }

    /// <summary>
    /// The parsed OsuFile for this entry (null for pause entries).
    /// </summary>
    public OsuFile? OsuFile { get; set; }

    /// <summary>
    /// Display title of the beatmap (or "Pause" for pause entries).
    /// </summary>
    public string Title => IsPause ? "Pause" : OsuFile?.Title ?? "Unknown";

    /// <summary>
    /// Creator/mapper of the beatmap (empty for pause entries).
    /// </summary>
    public string Creator => IsPause ? "" : OsuFile?.Creator ?? "";

    /// <summary>
    /// Difficulty version name (duration display for pause entries).
    /// </summary>
    public string Version => IsPause ? $"{PauseDurationSeconds:F1}s" : OsuFile?.Version ?? "";

    /// <summary>
    /// Playback rate multiplier (editable by user, default 1.0).
    /// </summary>
    public double Rate { get; set; } = 1.0;

    /// <summary>
    /// Calculated BPM based on dominant BPM and rate.
    /// </summary>
    public double Bpm => DominantBpm * Rate;

    /// <summary>
    /// The dominant BPM of the original beatmap.
    /// </summary>
    public double DominantBpm { get; set; }

    /// <summary>
    /// MSD skillset scores at the selected rate (null if not calculated or not 4K mania).
    /// </summary>
    public SkillsetScores? MsdValues { get; set; }

    /// <summary>
    /// MD5 hash of the beatmap file.
    /// </summary>
    public string FileHash { get; set; } = string.Empty;

    /// <summary>
    /// Time of the first note in milliseconds (parsed from HitObjects).
    /// </summary>
    public double FirstNoteTime { get; set; }

    /// <summary>
    /// End time of the last note/hold in milliseconds (parsed from HitObjects).
    /// </summary>
    public double LastNoteEndTime { get; set; }

    /// <summary>
    /// The effective duration of the map content (last note - first note), or pause duration in ms.
    /// </summary>
    public double EffectiveDuration => IsPause ? PauseDurationSeconds * 1000 : LastNoteEndTime - FirstNoteTime;

    /// <summary>
    /// The effective duration after rate is applied (pauses are not affected by rate).
    /// </summary>
    public double EffectiveDurationAtRate => IsPause ? PauseDurationSeconds * 1000 : EffectiveDuration / Rate;

    /// <summary>
    /// Unique identifier for list ordering/tracking.
    /// </summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// Gets a formatted display string for the rate/BPM (empty for pauses).
    /// </summary>
    public string RateBpmDisplay => IsPause ? "" : $"{Rate:0.0#}x / {Bpm:F0}bpm";

    /// <summary>
    /// Gets a formatted display string for MSD (or "--" if not available, empty for pauses).
    /// </summary>
    public string MsdDisplay => IsPause ? "" : (MsdValues != null ? $"{MsdValues.Overall:F1} {MsdValues.Stream:F1} {MsdValues.Jumpstream:F1} {MsdValues.Handstream:F1} {MsdValues.Stamina:F1} {MsdValues.Jackspeed:F1} {MsdValues.Chordjack:F1} {MsdValues.Technical:F1} " : "--");

    /// <summary>
    /// Gets a truncated file hash for display (first 8 characters, empty for pauses).
    /// </summary>
    public string TruncatedHash => IsPause ? "" : (FileHash.Length > 8 ? FileHash[..8] : FileHash);

    /// <summary>
    /// Calculates and sets the MD5 hash of the beatmap file.
    /// </summary>
    public void ComputeFileHash()
    {
        if (IsPause || OsuFile == null || string.IsNullOrEmpty(OsuFile.FilePath) || !File.Exists(OsuFile.FilePath))
        {
            FileHash = string.Empty;
            return;
        }

        try
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(OsuFile.FilePath);
            var hash = md5.ComputeHash(stream);
            FileHash = Convert.ToHexString(hash);
        }
        catch
        {
            FileHash = string.Empty;
        }
    }

    /// <summary>
    /// Creates a pause entry with the specified duration.
    /// </summary>
    /// <param name="durationSeconds">Duration of the pause in seconds.</param>
    /// <returns>A new MarathonEntry representing a pause.</returns>
    public static MarathonEntry CreatePause(double durationSeconds)
    {
        return new MarathonEntry
        {
            IsPause = true,
            PauseDurationSeconds = Math.Max(0.1, durationSeconds),
            OsuFile = null,
            Rate = 1.0,
            DominantBpm = 0,
            MsdValues = null,
            FileHash = string.Empty,
            FirstNoteTime = 0,
            LastNoteEndTime = 0
        };
    }

    /// <summary>
    /// Creates a MarathonEntry from an OsuFile, parsing note boundaries.
    /// </summary>
    /// <param name="osuFile">The parsed OsuFile.</param>
    /// <param name="hitObjects">Pre-parsed hit objects (optional, will parse if null).</param>
    /// <returns>A new MarathonEntry with all calculated values.</returns>
    public static MarathonEntry FromOsuFile(OsuFile osuFile, List<HitObject>? hitObjects = null)
    {
        var entry = new MarathonEntry
        {
            IsPause = false,
            OsuFile = osuFile
        };

        // Calculate dominant BPM
        entry.DominantBpm = CalculateDominantBpm(osuFile.TimingPoints);

        // Parse hit objects if not provided
        if (hitObjects == null && osuFile.RawSections.TryGetValue("HitObjects", out var hitObjectLines))
        {
            var keyCount = (int)osuFile.CircleSize;
            hitObjects = new List<HitObject>();
            foreach (var line in hitObjectLines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                    continue;
                var hitObj = HitObject.Parse(line, keyCount);
                if (hitObj != null)
                    hitObjects.Add(hitObj);
            }
        }

        // Calculate first and last note times
        if (hitObjects != null && hitObjects.Count > 0)
        {
            entry.FirstNoteTime = hitObjects.Min(h => h.Time);
            entry.LastNoteEndTime = hitObjects.Max(h => h.EndTime);
        }

        // Compute file hash
        entry.ComputeFileHash();

        return entry;
    }

    /// <summary>
    /// Calculates the dominant BPM from timing points.
    /// </summary>
    private static double CalculateDominantBpm(List<TimingPoint> timingPoints)
    {
        var uninherited = timingPoints.Where(tp => tp.Uninherited && tp.BeatLength > 0).ToList();
        if (uninherited.Count == 0) return 120;
        if (uninherited.Count == 1) return uninherited[0].Bpm;

        // Find BPM with longest duration
        var bpmDurations = new Dictionary<double, double>();
        for (int i = 0; i < uninherited.Count; i++)
        {
            var bpm = Math.Round(uninherited[i].Bpm, 1);
            var duration = i < uninherited.Count - 1
                ? uninherited[i + 1].Time - uninherited[i].Time
                : 60000;
            if (!bpmDurations.ContainsKey(bpm)) bpmDurations[bpm] = 0;
            bpmDurations[bpm] += duration;
        }

        return bpmDurations.OrderByDescending(kvp => kvp.Value).First().Key;
    }
}

