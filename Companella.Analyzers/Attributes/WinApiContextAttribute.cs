namespace Companella.Analyzers.Attributes;

/// <summary>
/// Indicates that the code within this context follows Windows API coding guidelines.
/// When present, analyzers will apply Windows API-specific naming conventions:
/// - Constants: SCREAMING_SNAKE_CASE (instead of PascalCase)
/// - Methods/Parameters: May follow Windows API naming conventions
/// </summary>
[AttributeUsage(
	AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Field |
	AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class WinApiContextAttribute : Attribute
{
}
