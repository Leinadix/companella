using System.Text.Json.Serialization;

namespace Companella.Models.Application;

/// <summary>
/// Represents a preset configuration for bulk rate changing.
/// </summary>
public class BulkRatePreset
{
	/// <summary>
	/// Display name for the preset.
	/// </summary>
	[JsonPropertyName("name")]
	public string Name { get; set; } = "Default";

	/// <summary>
	/// Minimum rate multiplier.
	/// </summary>
	[JsonPropertyName("minRate")]
	public double MinRate { get; set; } = 0.8;

	/// <summary>
	/// Maximum rate multiplier.
	/// </summary>
	[JsonPropertyName("maxRate")]
	public double MaxRate { get; set; } = 1.4;

	/// <summary>
	/// Rate step increment.
	/// </summary>
	[JsonPropertyName("step")]
	public double Step { get; set; } = 0.1;

	/// <summary>
	/// Overall Difficulty value. Null means use the map's original value.
	/// </summary>
	[JsonPropertyName("od")]
	public double? OD { get; set; }

	/// <summary>
	/// HP Drain Rate value. Null means use the map's original value.
	/// </summary>
	[JsonPropertyName("hp")]
	public double? HP { get; set; }

	/// <summary>
	/// Whether to exclude the base rate (1.0x) from generation.
	/// </summary>
	[JsonPropertyName("excludeBaseRate")]
	public bool ExcludeBaseRate { get; set; } = false;

	/// <summary>
	/// Creates a new preset with default values.
	/// </summary>
	public BulkRatePreset()
	{
	}

	/// <summary>
	/// Creates a new preset with the specified values.
	/// </summary>
	public BulkRatePreset(string name, double minRate, double maxRate, double step, double? od = null,
		double? hp = null, bool excludeBaseRate = false)
	{
		Name = name;
		MinRate = minRate;
		MaxRate = maxRate;
		Step = step;
		OD = od;
		HP = hp;
		ExcludeBaseRate = excludeBaseRate;
	}

	/// <summary>
	/// Gets the default presets.
	/// </summary>
	public static List<BulkRatePreset> GetDefaults()
	{
		return new List<BulkRatePreset>
		{
			new("All in one", 0.6, 1.7, 0.05),
			new("Default", 0.8, 1.4, 0.1),
			new("Default enhanced", 0.8, 1.45, 0.05),
			new("Extreme scaling", 0.9, 1.1, 0.01)
		};
	}

	/// <summary>
	/// Gets a formatted subtitle showing the rate range.
	/// </summary>
	public string GetSubtitle()
	{
		return $"{MinRate}x - {MaxRate}x";
	}

	/// <summary>
	/// Gets a tooltip description.
	/// </summary>
	public string GetTooltip()
	{
		return $"Create rates from {MinRate}x to {MaxRate}x with {Step} step";
	}
}
