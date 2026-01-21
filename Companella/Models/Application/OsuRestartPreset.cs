using System.Text.Json.Serialization;

namespace Companella.Models.Application;

/// <summary>
/// Represents a preset configuration for osu! restart with command line arguments.
/// </summary>
public class OsuRestartPreset
{
    /// <summary>
    /// Display name for the preset.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Default";

    /// <summary>
    /// Command line arguments to pass to osu! on startup.
    /// </summary>
    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = "";

    /// <summary>
    /// Creates a new preset with default values.
    /// </summary>
    public OsuRestartPreset() { }

    /// <summary>
    /// Creates a new preset with the specified values.
    /// </summary>
    public OsuRestartPreset(string name, string arguments)
    {
        Name = name;
        Arguments = arguments;
    }

    /// <summary>
    /// Gets the default presets.
    /// </summary>
    public static List<OsuRestartPreset> GetDefaults()
    {
        return new List<OsuRestartPreset>
        {
            new("Bancho", ""),
            new("Mames", "-devserver mamesosu.net")
        };
    }

    /// <summary>
    /// Gets a display string for the preset.
    /// </summary>
    public string GetDisplayText()
    {
        if (string.IsNullOrEmpty(Arguments))
            return $"{Name} (no args)";
        return $"{Name}: {Arguments}";
    }

    /// <summary>
    /// Gets a tooltip description.
    /// </summary>
    public string GetTooltip()
    {
        if (string.IsNullOrEmpty(Arguments))
            return "Start osu! without any command line arguments";
        return $"Start osu! with: {Arguments}";
    }

    public override string ToString() => Name;
}
