using Companella.Models.Beatmap;
using Companella.Models.Difficulty;

namespace Companella.Services;

/// <summary>
/// Helper service to calculate BPM at any given time from timing points.
/// </summary>
public class BpmCalculator
{
    private readonly List<TimingPoint> _timingPoints;
    private readonly List<TimingPoint> _uninheritedPoints;

    /// <summary>
    /// Creates a new BpmCalculator from a list of timing points.
    /// </summary>
    public BpmCalculator(List<TimingPoint> timingPoints)
    {
        _timingPoints = timingPoints.OrderBy(t => t.Time).ToList();
        _uninheritedPoints = _timingPoints.Where(t => t.Uninherited).ToList();
    }

    /// <summary>
    /// Creates a new BpmCalculator from an OsuFile.
    /// </summary>
    public BpmCalculator(OsuFile osuFile) : this(osuFile.TimingPoints)
    {
    }

    /// <summary>
    /// Gets the BPM at a specific time in milliseconds.
    /// </summary>
    /// <param name="timeMs">Time in milliseconds.</param>
    /// <returns>BPM at the specified time, or 0 if no timing point exists.</returns>
    public double GetBpmAtTime(double timeMs)
    {
        if (_uninheritedPoints.Count == 0)
            return 0;

        // Find the active uninherited timing point at this time
        TimingPoint? activePoint = null;

        foreach (var tp in _uninheritedPoints)
        {
            if (tp.Time <= timeMs)
            {
                activePoint = tp;
            }
            else
            {
                break; // Points are sorted, no need to continue
            }
        }

        // If no point before this time, use the first one
        activePoint ??= _uninheritedPoints[0];

        return activePoint.Bpm;
    }

    /// <summary>
    /// Gets the beat length (ms per beat) at a specific time.
    /// </summary>
    public double GetBeatLengthAtTime(double timeMs)
    {
        var bpm = GetBpmAtTime(timeMs);
        return bpm > 0 ? 60000.0 / bpm : 0;
    }

    /// <summary>
    /// Gets the timing point active at a specific time.
    /// </summary>
    public TimingPoint? GetTimingPointAtTime(double timeMs)
    {
        if (_uninheritedPoints.Count == 0)
            return null;

        TimingPoint? activePoint = null;

        foreach (var tp in _uninheritedPoints)
        {
            if (tp.Time <= timeMs)
            {
                activePoint = tp;
            }
            else
            {
                break;
            }
        }

        return activePoint ?? _uninheritedPoints[0];
    }

    /// <summary>
    /// Gets the interval in milliseconds for a specific note subdivision.
    /// </summary>
    /// <param name="timeMs">Time in milliseconds.</param>
    /// <param name="subdivision">Note subdivision (4 = quarter note, 8 = eighth note, etc.).</param>
    public double GetIntervalAtTime(double timeMs, int subdivision = 4)
    {
        var beatLength = GetBeatLengthAtTime(timeMs);
        return beatLength * (4.0 / subdivision);
    }

    /// <summary>
    /// Calculates the snap tolerance in milliseconds for a given subdivision at a specific time.
    /// </summary>
    /// <param name="timeMs">Time in milliseconds.</param>
    /// <param name="subdivision">Note subdivision (4, 8, 16, etc.).</param>
    /// <param name="tolerancePercent">Tolerance as percentage of interval (default 10%).</param>
    public double GetSnapTolerance(double timeMs, int subdivision, double tolerancePercent = 0.1)
    {
        var interval = GetIntervalAtTime(timeMs, subdivision);
        return interval * tolerancePercent;
    }

    /// <summary>
    /// Determines if two times are within a snap tolerance of each other.
    /// </summary>
    public bool IsWithinSnap(double time1, double time2, double toleranceMs)
    {
        return Math.Abs(time1 - time2) <= toleranceMs;
    }

    /// <summary>
    /// Gets the BPM range across the entire map.
    /// </summary>
    public (double Min, double Max) GetBpmRange()
    {
        if (_uninheritedPoints.Count == 0)
            return (0, 0);

        var bpms = _uninheritedPoints.Select(t => t.Bpm).ToList();
        return (bpms.Min(), bpms.Max());
    }

    /// <summary>
    /// Gets the average BPM of the map (simple average of all timing points).
    /// </summary>
    public double GetAverageBpm()
    {
        if (_uninheritedPoints.Count == 0)
            return 0;

        return _uninheritedPoints.Average(t => t.Bpm);
    }

    /// <summary>
    /// Gets the dominant BPM (most common or longest duration).
    /// </summary>
    public double GetDominantBpm()
    {
        if (_uninheritedPoints.Count == 0)
            return 0;

        if (_uninheritedPoints.Count == 1)
            return _uninheritedPoints[0].Bpm;

        // Calculate duration each timing point is active
        var durations = new Dictionary<double, double>();
        
        for (int i = 0; i < _uninheritedPoints.Count; i++)
        {
            var tp = _uninheritedPoints[i];
            var bpm = Math.Round(tp.Bpm, 1); // Round to avoid floating point issues

            double duration;
            if (i < _uninheritedPoints.Count - 1)
            {
                duration = _uninheritedPoints[i + 1].Time - tp.Time;
            }
            else
            {
                // Last timing point - assume it's active for at least some duration
                duration = 10000; // 10 seconds default for last section
            }

            if (durations.ContainsKey(bpm))
                durations[bpm] += duration;
            else
                durations[bpm] = duration;
        }

        return durations.OrderByDescending(d => d.Value).First().Key;
    }

    /// <summary>
    /// Checks if a time interval matches a specific note subdivision.
    /// </summary>
    public bool IsIntervalMatch(double interval, double timeMs, int subdivision, double toleranceMs = 5)
    {
        var expected = GetIntervalAtTime(timeMs, subdivision);
        return Math.Abs(interval - expected) <= toleranceMs;
    }

    /// <summary>
    /// Gets the estimated subdivision for a given interval at a specific time.
    /// </summary>
    public int EstimateSubdivision(double interval, double timeMs)
    {
        var beatLength = GetBeatLengthAtTime(timeMs);
        if (beatLength <= 0 || interval <= 0)
            return 0;

        // Check common subdivisions: 1, 2, 3, 4, 6, 8, 12, 16, 24, 32
        var subdivisions = new[] { 1, 2, 3, 4, 6, 8, 12, 16, 24, 32 };
        
        foreach (var sub in subdivisions)
        {
            var expected = beatLength * (4.0 / sub);
            var tolerance = expected * 0.15; // 15% tolerance

            if (Math.Abs(interval - expected) <= tolerance)
                return sub;
        }

        return 0; // Unknown subdivision
    }
}
