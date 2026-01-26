namespace Companella.Models.Training;

/// <summary>
/// Focus modes for map recommendations.
/// </summary>
public enum RecommendationFocus
{
	/// <summary>
	/// Focus on a specific skillset (stream, jumpstream, etc.).
	/// </summary>
	Skillset,

	/// <summary>
	/// Focus on improving consistency and accuracy on maps at current skill level.
	/// </summary>
	Consistency,

	/// <summary>
	/// Focus on pushing skill limits with maps slightly above current level.
	/// </summary>
	Push,

	/// <summary>
	/// Focus on fixing weakest skillsets to balance overall skill.
	/// </summary>
	DeficitFixing
}
