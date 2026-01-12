using System.Globalization;
using System.Numerics;

namespace Companella.Mods.Parameters;

/// <summary>
/// Generic numeric parameter supporting int, float, and double types.
/// </summary>
/// <typeparam name="T">The numeric type (int, float, or double).</typeparam>
public class ModParameter<T> : IModParameter where T : struct, INumber<T>
{
    private T _value;

    /// <summary>
    /// Display name shown in the UI.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Description shown as tooltip.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// The underlying value type.
    /// </summary>
    public Type ParameterType => typeof(T);

    /// <summary>
    /// Current value of the parameter.
    /// </summary>
    public T Value
    {
        get => _value;
        set => _value = Clamp(value);
    }

    /// <summary>
    /// Default value for reset.
    /// </summary>
    public T DefaultValue { get; }

    /// <summary>
    /// Minimum allowed value.
    /// </summary>
    public T MinValue { get; }

    /// <summary>
    /// Maximum allowed value.
    /// </summary>
    public T MaxValue { get; }

    /// <summary>
    /// Step increment for the slider.
    /// </summary>
    public T Step { get; }

    /// <summary>
    /// Number of decimal places to display (0 for integers).
    /// </summary>
    public int DecimalPlaces { get; }

    /// <summary>
    /// Whether this parameter uses discrete integer steps.
    /// </summary>
    public bool IsDiscrete => typeof(T) == typeof(int) || Step == T.One;

    /// <summary>
    /// Creates a new numeric parameter.
    /// </summary>
    /// <param name="name">Display name.</param>
    /// <param name="description">Tooltip description.</param>
    /// <param name="defaultValue">Default and initial value.</param>
    /// <param name="minValue">Minimum allowed value.</param>
    /// <param name="maxValue">Maximum allowed value.</param>
    /// <param name="step">Step increment (default 1).</param>
    /// <param name="decimalPlaces">Decimal places for display (auto-detected if not specified).</param>
    public ModParameter(
        string name,
        string description,
        T defaultValue,
        T minValue,
        T maxValue,
        T? step = null,
        int? decimalPlaces = null)
    {
        Name = name;
        Description = description;
        DefaultValue = defaultValue;
        MinValue = minValue;
        MaxValue = maxValue;
        Step = step ?? T.One;
        
        // Auto-detect decimal places based on type
        if (decimalPlaces.HasValue)
        {
            DecimalPlaces = decimalPlaces.Value;
        }
        else if (typeof(T) == typeof(int))
        {
            DecimalPlaces = 0;
        }
        else if (typeof(T) == typeof(float))
        {
            DecimalPlaces = 2;
        }
        else
        {
            DecimalPlaces = 3;
        }

        _value = Clamp(defaultValue);
    }

    /// <summary>
    /// Clamps a value to the valid range.
    /// </summary>
    private T Clamp(T value)
    {
        if (value < MinValue) return MinValue;
        if (value > MaxValue) return MaxValue;
        return value;
    }

    /// <inheritdoc />
    public object GetValue() => Value;

    /// <inheritdoc />
    public void SetValue(object value)
    {
        if (value is T typedValue)
        {
            Value = typedValue;
        }
        else
        {
            // Try to convert
            Value = (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
        }
    }

    /// <inheritdoc />
    public void Reset() => Value = DefaultValue;

    /// <inheritdoc />
    public object GetDefaultValue() => DefaultValue;

    /// <inheritdoc />
    public object? GetMinValue() => MinValue;

    /// <inheritdoc />
    public object? GetMaxValue() => MaxValue;

    /// <inheritdoc />
    public object? GetStep() => Step;

    /// <inheritdoc />
    public IReadOnlyList<(object Value, string DisplayName)>? GetEnumValues() => null;

    /// <inheritdoc />
    public double GetNormalizedValue()
    {
        var range = double.CreateChecked(MaxValue) - double.CreateChecked(MinValue);
        if (range == 0) return 0;
        return (double.CreateChecked(Value) - double.CreateChecked(MinValue)) / range;
    }

    /// <inheritdoc />
    public void SetNormalizedValue(double normalized)
    {
        normalized = Math.Clamp(normalized, 0, 1);
        var range = double.CreateChecked(MaxValue) - double.CreateChecked(MinValue);
        var rawValue = double.CreateChecked(MinValue) + (normalized * range);

        // Snap to step
        var stepValue = double.CreateChecked(Step);
        if (stepValue > 0)
        {
            var minVal = double.CreateChecked(MinValue);
            rawValue = minVal + Math.Round((rawValue - minVal) / stepValue) * stepValue;
        }

        Value = T.CreateChecked(rawValue);
    }

    /// <inheritdoc />
    public string GetDisplayValue()
    {
        if (DecimalPlaces == 0)
        {
            return Value.ToString() ?? "0";
        }
        
        var format = "F" + DecimalPlaces;
        return double.CreateChecked(Value).ToString(format, CultureInfo.InvariantCulture);
    }

    public override string ToString() => $"{Name}: {GetDisplayValue()}";
}
