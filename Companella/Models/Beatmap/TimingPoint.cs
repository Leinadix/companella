using System.Globalization;

namespace Companella.Models.Beatmap;

/// <summary>
/// Represents a timing point in an osu! beatmap.
/// </summary>
public class TimingPoint
{
    /// <summary>
    /// Start time of the timing section in milliseconds.
    /// </summary>
    public double Time { get; set; }

    /// <summary>
    /// For uninherited timing points: duration of a beat in milliseconds (60000 / BPM).
    /// For inherited timing points: negative inverse slider velocity multiplier as percentage.
    /// </summary>
    public double BeatLength { get; set; }

    /// <summary>
    /// Number of beats in a measure (time signature numerator).
    /// </summary>
    public int Meter { get; set; } = 4;

    /// <summary>
    /// Default sample set for hit objects (0=default, 1=normal, 2=soft, 3=drum).
    /// </summary>
    public int SampleSet { get; set; } = 1;

    /// <summary>
    /// Custom sample index for hit objects.
    /// </summary>
    public int SampleIndex { get; set; } = 0;

    /// <summary>
    /// Volume percentage for hit objects.
    /// </summary>
    public int Volume { get; set; } = 100;

    /// <summary>
    /// Whether this is an uninherited (true) or inherited (false) timing point.
    /// Uninherited = red line (BPM change), Inherited = green line (SV change).
    /// </summary>
    public bool Uninherited { get; set; } = true;

    /// <summary>
    /// Bit flags for effects (1=Kiai, 8=OmitFirstBarLine).
    /// </summary>
    public int Effects { get; set; } = 0;

    /// <summary>
    /// Gets the BPM for uninherited timing points.
    /// </summary>
    public double Bpm => Uninherited && BeatLength > 0 ? 60000.0 / BeatLength : 0;

    /// <summary>
    /// Creates an uninherited timing point from BPM.
    /// </summary>
    public static TimingPoint FromBpm(double timeMs, double bpm, int meter = 4)
    {
        return new TimingPoint
        {
            Time = timeMs,
            BeatLength = 60000.0 / bpm,
            Meter = meter,
            Uninherited = true
        };
    }

    /// <summary>
    /// Parses a timing point from .osu file format.
    /// </summary>
    public static TimingPoint Parse(string line)
    {
        var parts = line.Split(',');
        if (parts.Length < 2)
            throw new FormatException($"Invalid timing point format: {line}");

        return new TimingPoint
        {
            Time = double.Parse(parts[0], CultureInfo.InvariantCulture),
            BeatLength = double.Parse(parts[1], CultureInfo.InvariantCulture),
            Meter = parts.Length > 2 ? int.Parse(parts[2], CultureInfo.InvariantCulture) : 4,
            SampleSet = parts.Length > 3 ? int.Parse(parts[3], CultureInfo.InvariantCulture) : 1,
            SampleIndex = parts.Length > 4 ? int.Parse(parts[4], CultureInfo.InvariantCulture) : 0,
            Volume = parts.Length > 5 ? int.Parse(parts[5], CultureInfo.InvariantCulture) : 100,
            Uninherited = parts.Length > 6 && parts[6] == "1",
            Effects = parts.Length > 7 ? int.Parse(parts[7], CultureInfo.InvariantCulture) : 0
        };
    }

    /// <summary>
    /// Converts the timing point to .osu file format.
    /// </summary>
    public override string ToString()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0},{1},{2},{3},{4},{5},{6},{7}",
            Time, BeatLength, Meter, SampleSet, SampleIndex, Volume, Uninherited ? 1 : 0, Effects);
    }
}
