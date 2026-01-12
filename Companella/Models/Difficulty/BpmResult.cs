using System.Text.Json.Serialization;

namespace Companella.Models.Difficulty;

/// <summary>
/// Represents the JSON output from bpm.exe analysis.
/// </summary>
public class BpmResult
{
    [JsonPropertyName("beats")]
    public List<BeatInfo> Beats { get; set; } = new();

    [JsonPropertyName("average_bpm")]
    public double? AverageBpm { get; set; }

    [JsonPropertyName("estimated_tempo")]
    public double? EstimatedTempo { get; set; }
}

/// <summary>
/// Represents a single beat with its timestamp and instantaneous BPM.
/// </summary>
public class BeatInfo
{
    /// <summary>
    /// Time in seconds from the start of the audio.
    /// </summary>
    [JsonPropertyName("time")]
    public double Time { get; set; }

    /// <summary>
    /// Instantaneous BPM at this beat.
    /// </summary>
    [JsonPropertyName("bpm")]
    public double Bpm { get; set; }

    /// <summary>
    /// Gets the time in milliseconds.
    /// </summary>
    public double TimeMs => Time * 1000.0;
}
