using System.Globalization;

namespace OsuMappingHelper.Models;

/// <summary>
/// Type of hit object in osu!mania.
/// </summary>
public enum HitObjectType
{
    /// <summary>
    /// Single tap note (circle).
    /// </summary>
    Circle,

    /// <summary>
    /// Long note (hold).
    /// </summary>
    Hold
}

/// <summary>
/// Represents a single hit object (note) in an osu!mania beatmap.
/// </summary>
public class HitObject
{
    /// <summary>
    /// Time of the hit object in milliseconds.
    /// </summary>
    public double Time { get; set; }

    /// <summary>
    /// Column index (0-based, e.g., 0-3 for 4K).
    /// </summary>
    public int Column { get; set; }

    /// <summary>
    /// Type of hit object (Circle or Hold).
    /// </summary>
    public HitObjectType Type { get; set; }

    /// <summary>
    /// End time for hold notes (same as Time for circles).
    /// </summary>
    public double EndTime { get; set; }

    /// <summary>
    /// Hit sound flags (0 = normal, 2 = whistle, 4 = finish, 8 = clap).
    /// </summary>
    public int HitSound { get; set; }

    /// <summary>
    /// Hit sample string (format: normalSet:additionSet:index:volume:filename).
    /// </summary>
    public string HitSample { get; set; } = "0:0:0:0:";

    /// <summary>
    /// The original raw line from the .osu file (for reference/debugging).
    /// </summary>
    public string? RawLine { get; set; }

    /// <summary>
    /// Duration of hold note in milliseconds (0 for circles).
    /// </summary>
    public double Duration => EndTime - Time;

    /// <summary>
    /// Whether this is a hold note.
    /// </summary>
    public bool IsHold => Type == HitObjectType.Hold;

    /// <summary>
    /// Converts X position to column index.
    /// </summary>
    public static int XToColumn(double x, int keyCount = 4)
    {
        // Column width = 512 / keyCount
        var columnWidth = 512.0 / keyCount;
        var column = (int)(x / columnWidth);
        return Math.Clamp(column, 0, keyCount - 1);
    }

    /// <summary>
    /// Converts column index to X position (center of column).
    /// </summary>
    public static int ColumnToX(int column, int keyCount = 4)
    {
        var columnWidth = 512.0 / keyCount;
        return (int)(column * columnWidth + columnWidth / 2);
    }

    /// <summary>
    /// Parses a hit object from .osu file format.
    /// Format: x,y,time,type,hitSound,endTime:hitSample (for holds)
    /// Format: x,y,time,type,hitSound,hitSample (for circles)
    /// </summary>
    public static HitObject? Parse(string line, int keyCount = 4)
    {
        var parts = line.Split(',');
        if (parts.Length < 5)
            return null;

        try
        {
            var x = double.Parse(parts[0], CultureInfo.InvariantCulture);
            var time = double.Parse(parts[2], CultureInfo.InvariantCulture);
            var typeFlags = int.Parse(parts[3], CultureInfo.InvariantCulture);
            var hitSound = int.Parse(parts[4], CultureInfo.InvariantCulture);

            var column = XToColumn(x, keyCount);

            // Check if hold note (type flag 128)
            var isHold = (typeFlags & 128) != 0;

            double endTime = time;
            string hitSample = "0:0:0:0:";

            if (isHold && parts.Length >= 6)
            {
                // Hold notes have format: x,y,time,type,hitSound,endTime:hitSample
                var lastPart = parts[5];
                var colonIndex = lastPart.IndexOf(':');
                if (colonIndex >= 0)
                {
                    var endTimePart = lastPart.Substring(0, colonIndex);
                    if (double.TryParse(endTimePart, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedEndTime))
                    {
                        endTime = parsedEndTime;
                    }
                    hitSample = lastPart.Substring(colonIndex + 1);
                }
                else if (double.TryParse(lastPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedEndTime))
                {
                    endTime = parsedEndTime;
                }
            }
            else if (!isHold && parts.Length >= 6)
            {
                // Circle notes have format: x,y,time,type,hitSound,hitSample
                hitSample = parts[5];
            }

            return new HitObject
            {
                Time = time,
                Column = column,
                Type = isHold ? HitObjectType.Hold : HitObjectType.Circle,
                EndTime = endTime,
                HitSound = hitSound,
                HitSample = hitSample,
                RawLine = line
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Converts this hit object back to .osu file format.
    /// </summary>
    /// <param name="keyCount">The key count (used to calculate X position).</param>
    /// <returns>The .osu formatted string for this hit object.</returns>
    public string ToOsuString(int keyCount = 4)
    {
        var x = ColumnToX(Column, keyCount);
        var y = 192; // Standard Y position for mania
        var time = (int)Math.Round(Time);
        
        // Type flags: 1 = circle, 128 = hold (mania), plus new combo flags if needed
        var typeFlags = Type == HitObjectType.Hold ? 128 : 1;

        if (Type == HitObjectType.Hold)
        {
            // Hold note format: x,y,time,type,hitSound,endTime:hitSample
            var endTime = (int)Math.Round(EndTime);
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5}:{6}",
                x, y, time, typeFlags, HitSound, endTime, HitSample);
        }
        else
        {
            // Circle format: x,y,time,type,hitSound,hitSample
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5}",
                x, y, time, typeFlags, HitSound, HitSample);
        }
    }

    /// <summary>
    /// Creates a shallow copy of this hit object.
    /// </summary>
    public HitObject Clone()
    {
        return new HitObject
        {
            Time = Time,
            Column = Column,
            Type = Type,
            EndTime = EndTime,
            HitSound = HitSound,
            HitSample = HitSample,
            RawLine = RawLine
        };
    }

    public override string ToString()
    {
        return $"[{Time:F0}ms] Col{Column} {Type}{(IsHold ? $" ({Duration:F0}ms)" : "")}";
    }
}
