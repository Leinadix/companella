namespace Companella.Mods.Parameters;

/// <summary>
/// Enum parameter that allows selection from a set of predefined values.
/// </summary>
/// <typeparam name="T">The enum type.</typeparam>
public class EnumModParameter<T> : IModParameter where T : struct, Enum
{
	private T _value;
	private readonly T[] _allowedValues;
	private readonly Dictionary<T, string> _displayNames;

	/// <summary>
	/// Display name shown in the UI.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// Description shown as tooltip.
	/// </summary>
	public string Description { get; }

	/// <summary>
	/// The underlying enum type.
	/// </summary>
	public Type ParameterType => typeof(T);

	/// <summary>
	/// Current value of the parameter.
	/// </summary>
	public T Value
	{
		get => _value;
		set
		{
			if (_allowedValues.Contains(value)) _value = value;
		}
	}

	/// <summary>
	/// Default value for reset.
	/// </summary>
	public T DefaultValue { get; }

	/// <summary>
	/// Enum parameters are always discrete.
	/// </summary>
	public bool IsDiscrete => true;

	/// <summary>
	/// Creates a new enum parameter with all enum values allowed.
	/// </summary>
	/// <param name="name">Display name.</param>
	/// <param name="description">Tooltip description.</param>
	/// <param name="defaultValue">Default and initial value.</param>
	/// <param name="displayNames">Optional custom display names for enum values.</param>
	public EnumModParameter(
		string name,
		string description,
		T defaultValue,
		Dictionary<T, string>? displayNames = null)
		: this(name, description, defaultValue, Enum.GetValues<T>(), displayNames)
	{
	}

	/// <summary>
	/// Creates a new enum parameter with specific allowed values.
	/// </summary>
	/// <param name="name">Display name.</param>
	/// <param name="description">Tooltip description.</param>
	/// <param name="defaultValue">Default and initial value.</param>
	/// <param name="allowedValues">The subset of enum values that are allowed.</param>
	/// <param name="displayNames">Optional custom display names for enum values.</param>
	public EnumModParameter(
		string name,
		string description,
		T defaultValue,
		T[] allowedValues,
		Dictionary<T, string>? displayNames = null)
	{
		Name = name;
		Description = description;
		DefaultValue = defaultValue;
		_allowedValues = allowedValues.Length > 0 ? allowedValues : Enum.GetValues<T>();
		_displayNames = displayNames ?? new Dictionary<T, string>();
		_value = _allowedValues.Contains(defaultValue) ? defaultValue : _allowedValues[0];
	}

	/// <summary>
	/// Gets the display name for an enum value.
	/// </summary>
	public string GetDisplayName(T value)
	{
		if (_displayNames.TryGetValue(value, out var name)) return name;

		// Convert enum name to display format (e.g., "SomeValue" -> "Some Value")
		return FormatEnumName(value.ToString());
	}

	/// <summary>
	/// Formats an enum name for display by adding spaces before capital letters.
	/// </summary>
	private static string FormatEnumName(string name)
	{
		if (string.IsNullOrEmpty(name))
			return name;

		var result = new System.Text.StringBuilder();
		result.Append(name[0]);

		for (var i = 1; i < name.Length; i++)
		{
			if (char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) result.Append(' ');

			result.Append(name[i]);
		}

		return result.ToString();
	}

	/// <inheritdoc />
	public object GetValue()
	{
		return Value;
	}

	/// <inheritdoc />
	public void SetValue(object value)
	{
		if (value is T typedValue)
		{
			Value = typedValue;
		}
		else if (value is int intValue)
		{
			// Handle setting by index
			if (intValue >= 0 && intValue < _allowedValues.Length) Value = _allowedValues[intValue];
		}
		else if (value is string stringValue && Enum.TryParse<T>(stringValue, out var parsed))
		{
			Value = parsed;
		}
	}

	/// <inheritdoc />
	public void Reset()
	{
		Value = DefaultValue;
	}

	/// <inheritdoc />
	public object GetDefaultValue()
	{
		return DefaultValue;
	}

	/// <inheritdoc />
	public object? GetMinValue()
	{
		return null;
	}

	/// <inheritdoc />
	public object? GetMaxValue()
	{
		return null;
	}

	/// <inheritdoc />
	public object? GetStep()
	{
		return null;
	}

	/// <inheritdoc />
	public IReadOnlyList<(object Value, string DisplayName)> GetEnumValues()
	{
		return _allowedValues
			.Select(v => ((object)v, GetDisplayName(v)))
			.ToList();
	}

	/// <inheritdoc />
	public double GetNormalizedValue()
	{
		var index = Array.IndexOf(_allowedValues, Value);
		if (index < 0)
			return 0;
		if (_allowedValues.Length <= 1)
			return 0;
		return (double)index / (_allowedValues.Length - 1);
	}

	/// <inheritdoc />
	public void SetNormalizedValue(double normalized)
	{
		normalized = Math.Clamp(normalized, 0, 1);
		var index = (int)Math.Round(normalized * (_allowedValues.Length - 1));
		index = Math.Clamp(index, 0, _allowedValues.Length - 1);
		Value = _allowedValues[index];
	}

	/// <inheritdoc />
	public string GetDisplayValue()
	{
		return GetDisplayName(Value);
	}

	public override string ToString()
	{
		return $"{Name}: {GetDisplayValue()}";
	}
}
