using System.Globalization;
using OsuMappingHelper.Models;

namespace OsuMappingHelper.Services;

/// <summary>
/// Normalizes scroll velocity (SV) in osu!mania beatmaps to maintain consistent scroll speed
/// regardless of BPM changes.
/// </summary>
public class SvNormalizer
{
    /// <summary>
    /// Normalizes the scroll velocity for all BPM changes in the beatmap.
    /// First removes ALL inherited timing points, then creates new ones to counteract BPM changes.
    /// </summary>
    /// <param name="timingPoints">Existing timing points from the beatmap.</param>
    /// <param name="baseBpm">The base BPM to normalize to. If null, uses the most common BPM.</param>
    /// <returns>List of timing points with SV normalization applied.</returns>
    public List<TimingPoint> Normalize(List<TimingPoint> timingPoints, double? baseBpm = null)
    {
        // Get all uninherited (red) timing points - these define BPM
        var uninherited = timingPoints.Where(tp => tp.Uninherited).OrderBy(tp => tp.Time).ToList();
        
        if (uninherited.Count == 0)
            return timingPoints;

        // Count existing inherited points for logging
        var existingInheritedCount = timingPoints.Count(tp => !tp.Uninherited);
        Logger.Info($"[SV Normalize] Removing {existingInheritedCount} existing inherited timing points");

        // Determine base BPM if not specified
        double targetBpm = baseBpm ?? DetermineBaseBpm(uninherited);
        Logger.Info($"[SV Normalize] Base BPM: {targetBpm:F2}");

        // Create result list starting with all uninherited points only (no inherited points)
        var result = new List<TimingPoint>();
        result.AddRange(uninherited);

        // For each uninherited timing point, create a corresponding inherited point for SV normalization
        foreach (var tp in uninherited)
        {
            double currentBpm = tp.Bpm;
            if (currentBpm <= 0) continue;

            // Calculate SV multiplier to normalize scroll speed
            // SV = BaseBPM / CurrentBPM
            double svMultiplier = targetBpm / currentBpm;
            
            // Clamp SV to valid range (0.1x to 10x)
            svMultiplier = Math.Clamp(svMultiplier, 0.1, 10.0);

            // Convert SV multiplier to inherited timing point BeatLength
            // In osu!, inherited BeatLength = -100 / SV_multiplier
            double inheritedBeatLength = -100.0 / svMultiplier;

            Logger.Info($"[SV Normalize] Time {tp.Time:F0}ms: BPM {currentBpm:F2} -> SV {svMultiplier:F4} (BeatLength: {inheritedBeatLength:F4})");

            // Create new inherited timing point for SV normalization
            var svPoint = new TimingPoint
            {
                Time = tp.Time,
                BeatLength = inheritedBeatLength,
                Meter = tp.Meter,
                SampleSet = tp.SampleSet,
                SampleIndex = tp.SampleIndex,
                Volume = tp.Volume,
                Uninherited = false,
                Effects = tp.Effects
            };
            result.Add(svPoint);
        }

        Logger.Info($"[SV Normalize] Created {uninherited.Count} SV points for normalization");

        // Sort by time (uninherited points first at same time)
        return result.OrderBy(tp => tp.Time).ThenByDescending(tp => tp.Uninherited).ToList();
    }

    /// <summary>
    /// Determines the most appropriate base BPM for normalization.
    /// Uses the BPM that covers the most time in the beatmap.
    /// </summary>
    private double DetermineBaseBpm(List<TimingPoint> uninherited)
    {
        if (uninherited.Count == 0)
            return 120; // Default

        if (uninherited.Count == 1)
            return uninherited[0].Bpm;

        // Calculate duration for each BPM section
        var bpmDurations = new Dictionary<double, double>();
        
        for (int i = 0; i < uninherited.Count; i++)
        {
            double bpm = Math.Round(uninherited[i].Bpm, 1); // Round to avoid floating point issues
            double startTime = uninherited[i].Time;
            double endTime = i < uninherited.Count - 1 ? uninherited[i + 1].Time : startTime + 60000; // Assume 1 minute if last section

            double duration = endTime - startTime;
            
            if (!bpmDurations.ContainsKey(bpm))
                bpmDurations[bpm] = 0;
            bpmDurations[bpm] += duration;
        }

        // Return BPM with longest total duration
        var dominantBpm = bpmDurations.OrderByDescending(kvp => kvp.Value).First().Key;
        Logger.Info($"[SV Normalize] Dominant BPM: {dominantBpm:F2} (covers {bpmDurations[dominantBpm] / 1000:F1}s)");
        
        return dominantBpm;
    }

    /// <summary>
    /// Removes all SV normalization from the beatmap (removes inherited points at BPM change locations).
    /// </summary>
    public List<TimingPoint> RemoveNormalization(List<TimingPoint> timingPoints)
    {
        var uninherited = timingPoints.Where(tp => tp.Uninherited).ToList();
        var uninheritedTimes = uninherited.Select(tp => tp.Time).ToHashSet();
        
        // Keep inherited points that are NOT at BPM change times
        var inherited = timingPoints
            .Where(tp => !tp.Uninherited && !uninheritedTimes.Any(t => Math.Abs(t - tp.Time) < 1))
            .ToList();

        var result = new List<TimingPoint>();
        result.AddRange(uninherited);
        result.AddRange(inherited);

        return result.OrderBy(tp => tp.Time).ThenByDescending(tp => tp.Uninherited).ToList();
    }

    /// <summary>
    /// Gets normalization statistics.
    /// </summary>
    public SvNormalizationStats GetStats(List<TimingPoint> original, List<TimingPoint> normalized, double baseBpm)
    {
        var originalInherited = original.Count(tp => !tp.Uninherited);
        var normalizedInherited = normalized.Count(tp => !tp.Uninherited);
        var bpmChanges = original.Where(tp => tp.Uninherited).Select(tp => tp.Bpm).Distinct().Count();

        return new SvNormalizationStats
        {
            BaseBpm = baseBpm,
            BpmChangesFound = bpmChanges,
            InheritedPointsRemoved = originalInherited,
            SvPointsCreated = normalizedInherited,
            MinSv = normalized.Where(tp => !tp.Uninherited).Select(tp => -100.0 / tp.BeatLength).DefaultIfEmpty(1).Min(),
            MaxSv = normalized.Where(tp => !tp.Uninherited).Select(tp => -100.0 / tp.BeatLength).DefaultIfEmpty(1).Max()
        };
    }
}

/// <summary>
/// Statistics about SV normalization.
/// </summary>
public class SvNormalizationStats
{
    public double BaseBpm { get; set; }
    public int BpmChangesFound { get; set; }
    public int InheritedPointsRemoved { get; set; }
    public int SvPointsCreated { get; set; }
    public double MinSv { get; set; }
    public double MaxSv { get; set; }
}
