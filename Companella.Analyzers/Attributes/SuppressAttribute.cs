namespace Companella.Analyzers.Attributes;

/// <summary>
/// Suppresses specific Companella analyzer rules.
/// </summary>
[AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
public sealed class SuppressAttribute : Attribute
{
	/// <summary>
	/// The diagnostic ID(s) to suppress. Can be a single ID or comma-separated list.
	/// </summary>
	public string DiagnosticIds { get; }

	/// <summary>
	/// Optional justification for suppressing the rule.
	/// </summary>
	public string? Justification { get; set; }

	/// <summary>
	/// Initializes a new instance of the <see cref="SuppressAttribute"/> class.
	/// </summary>
	/// <param name="diagnosticIds">The diagnostic ID(s) to suppress.</param>
	public SuppressAttribute(string diagnosticIds)
	{
		DiagnosticIds = diagnosticIds ?? throw new ArgumentNullException(nameof(diagnosticIds));
	}
}
