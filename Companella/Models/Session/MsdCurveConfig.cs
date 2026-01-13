namespace Companella.Models.Session;

/// <summary>
/// Modes for generating session curves from historical data.
/// </summary>
public enum SessionGenerationMode
{
    /// <summary>
    /// Standard session pattern analysis.
    /// </summary>
    Normal,

    /// <summary>
    /// Focus on strongest skillsets with +1 MSD boost.
    /// </summary>
    Push,

    /// <summary>
    /// Target MSD ranges where user achieved 98%+ accuracy.
    /// </summary>
    Acc,

    /// <summary>
    /// Focus on weakest skillsets with +1 MSD boost.
    /// </summary>
    Fix
}

/// <summary>
/// Represents a single control point on the MSD curve.
/// </summary>
public class MsdControlPoint
{
    /// <summary>
    /// Position on the time axis as a percentage (0-100).
    /// </summary>
    public double TimePercent { get; set; }

    /// <summary>
    /// Absolute MSD value at this point (0-50 range).
    /// </summary>
    public double Msd { get; set; }

    /// <summary>
    /// Focus skillset for this segment of the session (null for any).
    /// </summary>
    public string? Skillset { get; set; }

    /// <summary>
    /// Creates a new control point.
    /// </summary>
    public MsdControlPoint(double timePercent, double msd, string? skillset = null)
    {
        TimePercent = Math.Clamp(timePercent, 0, 100);
        Msd = Math.Clamp(msd, 0, 50);
        Skillset = skillset;
    }

    /// <summary>
    /// Creates a copy of this control point.
    /// </summary>
    public MsdControlPoint Clone() => new MsdControlPoint(TimePercent, Msd, Skillset);
}

/// <summary>
/// Configuration for the MSD curve used in session planning.
/// Defines how MSD changes over the duration of a session.
/// Y-axis shows absolute MSD values from 0-50.
/// </summary>
public class MsdCurveConfig
{
    private readonly List<MsdControlPoint> _points = new();

    /// <summary>
    /// Total session duration in minutes.
    /// </summary>
    public int TotalSessionMinutes { get; set; } = 80;

    /// <summary>
    /// Read-only access to the control points, sorted by TimePercent.
    /// </summary>
    public IReadOnlyList<MsdControlPoint> Points => _points.AsReadOnly();

    /// <summary>
    /// Minimum number of control points required.
    /// </summary>
    public const int MinimumPoints = 2;

    /// <summary>
    /// Creates a new MsdCurveConfig with default curve.
    /// </summary>
    public MsdCurveConfig()
    {
        SetDefaultCurve();
    }

    /// <summary>
    /// Sets the curve to a default shape (warmup -> peak -> cooldown).
    /// Default values assume ~20 MSD skill level.
    /// </summary>
    public void SetDefaultCurve()
    {
        _points.Clear();
        _points.Add(new MsdControlPoint(0, 18));       // Warmup start
        _points.Add(new MsdControlPoint(18.75, 18));   // Warmup end
        _points.Add(new MsdControlPoint(75, 23));      // Ramp-up peak
        _points.Add(new MsdControlPoint(100, 20));     // Cooldown end
    }

    /// <summary>
    /// Adds a new control point to the curve.
    /// </summary>
    /// <param name="timePercent">Position on time axis (0-100).</param>
    /// <param name="msd">Absolute MSD value (0-50).</param>
    /// <param name="skillset">Optional focus skillset for this point.</param>
    /// <returns>The added point, or null if a point already exists at that time.</returns>
    public MsdControlPoint? AddPoint(double timePercent, double msd, string? skillset = null)
    {
        timePercent = Math.Clamp(timePercent, 0, 100);

        // Check if a point already exists very close to this time
        if (_points.Any(p => Math.Abs(p.TimePercent - timePercent) < 0.5))
            return null;

        var point = new MsdControlPoint(timePercent, msd, skillset);
        _points.Add(point);
        SortPoints();
        return point;
    }

    /// <summary>
    /// Gets the skillset at a specific time (from the nearest previous point).
    /// </summary>
    /// <param name="timePercent">Time position (0-100).</param>
    /// <returns>The skillset, or null if no skillset is set.</returns>
    public string? GetSkillsetAtTime(double timePercent)
    {
        if (_points.Count == 0)
            return null;

        timePercent = Math.Clamp(timePercent, 0, 100);

        // Find the point at or just before this time
        MsdControlPoint? activePoint = null;
        foreach (var point in _points)
        {
            if (point.TimePercent <= timePercent)
                activePoint = point;
            else
                break;
        }

        return activePoint?.Skillset;
    }

    /// <summary>
    /// Removes a control point from the curve.
    /// </summary>
    /// <param name="point">The point to remove.</param>
    /// <returns>True if removed, false if not found or would violate minimum points.</returns>
    public bool RemovePoint(MsdControlPoint point)
    {
        if (_points.Count <= MinimumPoints)
            return false;

        return _points.Remove(point);
    }

    /// <summary>
    /// Removes a control point at the specified index.
    /// </summary>
    /// <param name="index">The index of the point to remove.</param>
    /// <returns>True if removed, false if invalid index or would violate minimum points.</returns>
    public bool RemovePointAt(int index)
    {
        if (_points.Count <= MinimumPoints)
            return false;

        if (index < 0 || index >= _points.Count)
            return false;

        _points.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// Updates a point's position and re-sorts the list.
    /// </summary>
    /// <param name="point">The point to update.</param>
    /// <param name="newTimePercent">New time position (0-100).</param>
    /// <param name="newMsd">New absolute MSD value (0-50).</param>
    public void UpdatePoint(MsdControlPoint point, double newTimePercent, double newMsd)
    {
        point.TimePercent = Math.Clamp(newTimePercent, 0, 100);
        point.Msd = Math.Clamp(newMsd, 0, 50);
        SortPoints();
    }

    /// <summary>
    /// Gets the MSD value at a specific time percentage using linear interpolation.
    /// </summary>
    /// <param name="timePercent">Time position (0-100).</param>
    /// <returns>The interpolated absolute MSD value.</returns>
    public double GetMsdAtTime(double timePercent)
    {
        if (_points.Count == 0)
            return 20; // Default MSD

        timePercent = Math.Clamp(timePercent, 0, 100);

        // Find the two points to interpolate between
        MsdControlPoint? before = null;
        MsdControlPoint? after = null;

        foreach (var point in _points)
        {
            if (point.TimePercent <= timePercent)
                before = point;
            else if (after == null)
                after = point;
        }

        // Edge cases
        if (before == null && after != null)
            return after.Msd;
        if (after == null && before != null)
            return before.Msd;
        if (before == null || after == null)
            return 20;

        // Exact match
        if (Math.Abs(before.TimePercent - timePercent) < 0.001)
            return before.Msd;

        // Linear interpolation
        var t = (timePercent - before.TimePercent) / (after.TimePercent - before.TimePercent);
        return before.Msd + t * (after.Msd - before.Msd);
    }

    /// <summary>
    /// Converts a time percentage to minutes based on session duration.
    /// </summary>
    public double TimePercentToMinutes(double timePercent)
    {
        return TotalSessionMinutes * (timePercent / 100.0);
    }

    /// <summary>
    /// Converts minutes to time percentage based on session duration.
    /// </summary>
    public double MinutesToTimePercent(double minutes)
    {
        return (minutes / TotalSessionMinutes) * 100.0;
    }

    /// <summary>
    /// Creates a deep copy of this configuration.
    /// </summary>
    public MsdCurveConfig Clone()
    {
        var clone = new MsdCurveConfig
        {
            TotalSessionMinutes = TotalSessionMinutes
        };
        clone._points.Clear();
        foreach (var point in _points)
        {
            clone._points.Add(point.Clone());
        }
        return clone;
    }

    /// <summary>
    /// Sorts points by TimePercent.
    /// </summary>
    private void SortPoints()
    {
        _points.Sort((a, b) => a.TimePercent.CompareTo(b.TimePercent));
    }

    /// <summary>
    /// Gets the minimum MSD value in the curve.
    /// </summary>
    public double MinMsd => _points.Count > 0 ? _points.Min(p => p.Msd) : 0;

    /// <summary>
    /// Gets the maximum MSD value in the curve.
    /// </summary>
    public double MaxMsd => _points.Count > 0 ? _points.Max(p => p.Msd) : 0;

    /// <summary>
    /// Gets the index of a control point in the list.
    /// </summary>
    /// <param name="point">The point to find.</param>
    /// <returns>The index, or -1 if not found.</returns>
    public int IndexOf(MsdControlPoint point)
    {
        return _points.IndexOf(point);
    }

    /// <summary>
    /// Generates a curve configuration based on historical session data.
    /// Analyzes the typical MSD pattern from past sessions, creating one point per 6 minutes.
    /// </summary>
    /// <param name="trends">The skill trends containing historical play data.</param>
    /// <param name="mode">The generation mode to use.</param>
    /// <param name="sessionDurationMinutes">The desired session duration in minutes.</param>
    /// <returns>A new curve config based on the analysis, or null if insufficient data.</returns>
    public static MsdCurveConfig? GenerateFromTrends(SkillsTrendResult trends, SessionGenerationMode mode = SessionGenerationMode.Normal, int sessionDurationMinutes = 80)
    {
        if (trends == null || trends.Plays.Count < 5)
            return null;

        var config = new MsdCurveConfig();
        config._points.Clear();
        config.TotalSessionMinutes = sessionDurationMinutes;

        // For Acc mode, use a different approach based on high-accuracy plays
        if (mode == SessionGenerationMode.Acc)
        {
            return GenerateAccMode(trends, config, sessionDurationMinutes);
        }

        // Determine skillset filter for Push/Fix modes
        HashSet<string>? skillsetFilter = null;
        List<string>? targetSkillsets = null;

        if (mode == SessionGenerationMode.Push)
        {
            targetSkillsets = trends.GetStrongestSkillsets(3);
            skillsetFilter = new HashSet<string>(targetSkillsets, StringComparer.OrdinalIgnoreCase);
        }
        else if (mode == SessionGenerationMode.Fix)
        {
            targetSkillsets = trends.GetWeakestSkillsets(3);
            skillsetFilter = new HashSet<string>(targetSkillsets, StringComparer.OrdinalIgnoreCase);
        }

        // Group plays by session
        var sessionGroups = trends.Plays
            .GroupBy(p => p.SessionId)
            .Where(g => g.Count() >= 3) // Only sessions with enough plays
            .ToList();

        if (sessionGroups.Count == 0)
            return null;

        // For each session, normalize play times to 0-100%
        var normalizedPlays = new List<(double timePercent, float msd, string skillset)>();

        foreach (var session in sessionGroups)
        {
            var plays = session.OrderBy(p => p.PlayedAt).ToList();
            var sessionStart = plays.First().PlayedAt;
            var sessionEnd = plays.Last().PlayedAt;
            var sessionDuration = (sessionEnd - sessionStart).TotalMinutes;

            if (sessionDuration < 5) // Skip very short sessions
                continue;

            foreach (var play in plays)
            {
                // For Push/Fix modes, filter by target skillsets
                if (skillsetFilter != null && !skillsetFilter.Contains(play.DominantSkillset))
                    continue;

                var elapsed = (play.PlayedAt - sessionStart).TotalMinutes;
                var timePercent = (elapsed / sessionDuration) * 100.0;
                normalizedPlays.Add((timePercent, play.HighestMsdValue, play.DominantSkillset));
            }
        }

        // For filtered modes, we may have fewer plays - lower the threshold
        var minPlays = skillsetFilter != null ? 5 : 10;
        if (normalizedPlays.Count < minPlays)
        {
            // If Push/Fix mode has too few plays, fall back to using all plays but with target skillset labels
            if (skillsetFilter != null)
            {
                normalizedPlays.Clear();
                foreach (var session in sessionGroups)
                {
                    var plays = session.OrderBy(p => p.PlayedAt).ToList();
                    var sessionStart = plays.First().PlayedAt;
                    var sessionEnd = plays.Last().PlayedAt;
                    var sessionDuration = (sessionEnd - sessionStart).TotalMinutes;

                    if (sessionDuration < 5) continue;

                    foreach (var play in plays)
                    {
                        var elapsed = (play.PlayedAt - sessionStart).TotalMinutes;
                        var timePercent = (elapsed / sessionDuration) * 100.0;
                        normalizedPlays.Add((timePercent, play.HighestMsdValue, play.DominantSkillset));
                    }
                }
            }

            if (normalizedPlays.Count < 5)
                return null;
        }

        // Create one point per 6 minutes of session time
        const double minutesPerPoint = 6.0;
        var pointCount = Math.Max(2, (int)Math.Ceiling(sessionDurationMinutes / minutesPerPoint) + 1);
        
        // Limit to reasonable number of points
        pointCount = Math.Min(pointCount, 20);

        // MSD boost for Push/Fix modes
        var msdBoost = (mode == SessionGenerationMode.Push || mode == SessionGenerationMode.Fix) ? 1.0 : 0.0;

        for (int i = 0; i < pointCount; i++)
        {
            var timePercent = (i / (double)(pointCount - 1)) * 100.0;
            
            // Define bucket range around this time point
            var bucketHalfWidth = 50.0 / (pointCount - 1); // Half the distance to next point
            var bucketStart = Math.Max(0, timePercent - bucketHalfWidth);
            var bucketEnd = Math.Min(100, timePercent + bucketHalfWidth);

            var bucketPlays = normalizedPlays
                .Where(p => p.timePercent >= bucketStart && p.timePercent <= bucketEnd)
                .ToList();

            double avgMsd;
            if (bucketPlays.Count == 0)
            {
                // Use overall skill level as fallback
                avgMsd = trends.OverallSkillLevel > 0 ? trends.OverallSkillLevel : 20;
            }
            else
            {
                // Calculate average MSD for this bucket (absolute value)
                avgMsd = bucketPlays.Average(p => p.msd);
            }

            // Apply MSD boost for Push/Fix modes
            avgMsd += msdBoost;

            // For Push/Fix modes, cycle through target skillsets
            string? pointSkillset = null;
            if (targetSkillsets != null && targetSkillsets.Count > 0)
            {
                pointSkillset = targetSkillsets[i % targetSkillsets.Count];
            }
            else
            {
                // Find most common skillset in this bucket
                var skillsetCounts = bucketPlays
                    .Where(p => !string.IsNullOrEmpty(p.skillset) && p.skillset != "unknown")
                    .GroupBy(p => p.skillset)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault();
                pointSkillset = skillsetCounts?.Key;
            }

            config._points.Add(new MsdControlPoint(timePercent, avgMsd, pointSkillset));
        }

        return config;
    }

    /// <summary>
    /// Generates a curve for Acc mode - targeting MSD ranges where user achieved 98%+ accuracy.
    /// </summary>
    private static MsdCurveConfig? GenerateAccMode(SkillsTrendResult trends, MsdCurveConfig config, int sessionDurationMinutes)
    {
        // Filter plays to high accuracy (98%+), fall back to 96%+ if not enough
        var highAccPlays = trends.Plays
            .Where(p => p.Accuracy >= 0.98)
            .ToList();

        if (highAccPlays.Count < 5)
        {
            highAccPlays = trends.Plays
                .Where(p => p.Accuracy >= 0.96)
                .ToList();
        }

        if (highAccPlays.Count < 5)
        {
            // Not enough high-acc plays, use lower percentile of all plays
            var sortedByMsd = trends.Plays.OrderBy(p => p.HighestMsdValue).ToList();
            var lowerThird = sortedByMsd.Take(sortedByMsd.Count / 3).ToList();
            if (lowerThird.Count >= 3)
                highAccPlays = lowerThird;
            else
                return null;
        }

        // Get MSD statistics from high-acc plays
        var msdValues = highAccPlays.Select(p => (double)p.HighestMsdValue).ToList();
        var minMsd = msdValues.Min();
        var maxMsd = msdValues.Max();
        var avgMsd = msdValues.Average();

        // Create curve that stays within the comfortable MSD range
        // Warmup at lower end, peak at average, cooldown back down
        const double minutesPerPoint = 6.0;
        var pointCount = Math.Max(2, (int)Math.Ceiling(sessionDurationMinutes / minutesPerPoint) + 1);
        pointCount = Math.Min(pointCount, 20);

        for (int i = 0; i < pointCount; i++)
        {
            var timePercent = (i / (double)(pointCount - 1)) * 100.0;

            // Create a gentle curve within the comfortable range
            // Start at min, rise to avg around 50-60%, then stay there or slightly decrease
            double msd;
            if (timePercent < 20)
            {
                // Warmup: min to slightly above min
                var t = timePercent / 20.0;
                msd = minMsd + (avgMsd - minMsd) * 0.3 * t;
            }
            else if (timePercent < 60)
            {
                // Ramp up: rise toward average
                var t = (timePercent - 20) / 40.0;
                msd = minMsd + (avgMsd - minMsd) * (0.3 + 0.7 * t);
            }
            else if (timePercent < 80)
            {
                // Peak: stay around average
                msd = avgMsd;
            }
            else
            {
                // Cooldown: decrease slightly
                var t = (timePercent - 80) / 20.0;
                msd = avgMsd - (avgMsd - minMsd) * 0.3 * t;
            }

            // Find dominant skillset from high-acc plays
            var skillsetCounts = highAccPlays
                .Where(p => !string.IsNullOrEmpty(p.DominantSkillset) && p.DominantSkillset != "unknown")
                .GroupBy(p => p.DominantSkillset)
                .OrderByDescending(g => g.Count())
                .ToList();

            string? pointSkillset = null;
            if (skillsetCounts.Count > 0)
            {
                // Cycle through top skillsets
                pointSkillset = skillsetCounts[i % skillsetCounts.Count].Key;
            }

            config._points.Add(new MsdControlPoint(timePercent, msd, pointSkillset));
        }

        return config;
    }
}
