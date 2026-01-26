namespace Companella.Mods.Parameters;

/// <summary>
/// Base interface for all mod parameters.
/// Parameters allow mods to expose configurable values that are rendered in the UI.
/// </summary>
public interface IModParameter
{
	/// <summary>
	/// Display name shown in the UI.
	/// </summary>
	string Name { get; }

	/// <summary>
	/// Description shown as tooltip.
	/// </summary>
	string Description { get; }

	/// <summary>
	/// The underlying value type (int, float, double, or enum type).
	/// </summary>
	Type ParameterType { get; }

	/// <summary>
	/// Gets the current value as object.
	/// </summary>
	object GetValue();

	/// <summary>
	/// Sets the value from an object.
	/// </summary>
	void SetValue(object value);

	/// <summary>
	/// Resets the parameter to its default value.
	/// </summary>
	void Reset();

	/// <summary>
	/// Gets the default value as object.
	/// </summary>
	object GetDefaultValue();

	/// <summary>
	/// Gets the minimum value as object (null for enums).
	/// </summary>
	object? GetMinValue();

	/// <summary>
	/// Gets the maximum value as object (null for enums).
	/// </summary>
	object? GetMaxValue();

	/// <summary>
	/// Gets the step increment as object (null for enums).
	/// </summary>
	object? GetStep();

	/// <summary>
	/// Whether this parameter uses discrete integer steps.
	/// </summary>
	bool IsDiscrete { get; }

	/// <summary>
	/// For enum parameters, gets the allowed values and their display names.
	/// Returns null for numeric parameters.
	/// </summary>
	IReadOnlyList<(object Value, string DisplayName)>? GetEnumValues();

	/// <summary>
	/// Gets the normalized value (0-1) for slider positioning.
	/// </summary>
	double GetNormalizedValue();

	/// <summary>
	/// Sets the value from a normalized (0-1) slider position.
	/// </summary>
	void SetNormalizedValue(double normalized);

	/// <summary>
	/// Gets a formatted string representation of the current value for display.
	/// </summary>
	string GetDisplayValue();
}
