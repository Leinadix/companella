using System.Diagnostics.CodeAnalysis;

namespace Companella.Models.Difficulty;

/// <summary>
/// Represents a BPM multiplication factor for analysis results.
/// </summary>
[SuppressMessage("", "CA1720")]
public enum BpmFactor
{
	/// <summary>
	/// Halve the detected BPM (0.5x).
	/// </summary>
	Half,

	/// <summary>
	/// Use the detected BPM as-is (1x).
	/// </summary>
	Normal,

	/// <summary>
	/// Double the detected BPM (2x).
	/// </summary>
	Double
}

/// <summary>
/// Extension methods for BpmFactor.
/// </summary>
public static class BpmFactorExtensions
{
	/// <summary>
	/// Gets the multiplier value for the factor.
	/// </summary>
	public static double GetMultiplier(this BpmFactor factor)
	{
		return factor switch
		{
			BpmFactor.Half => 0.5,
			BpmFactor.Normal => 1.0,
			BpmFactor.Double => 2.0,
			_ => 1.0
		};
	}

	/// <summary>
	/// Gets a display label for the factor.
	/// </summary>
	public static string GetLabel(this BpmFactor factor)
	{
		return factor switch
		{
			BpmFactor.Half => "0.5x",
			BpmFactor.Normal => "1x",
			BpmFactor.Double => "2x",
			_ => "1x"
		};
	}
}
