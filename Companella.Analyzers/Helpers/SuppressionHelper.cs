using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Companella.Analyzers.Helpers;

/// <summary>
/// Helper class for checking if diagnostics should be suppressed based on custom attributes.
/// </summary>
internal static class SuppressionHelper
{
	private const string SuppressAttributeName = "Companella.Analyzers.Attributes.SuppressAttribute";
	private const string SuppressAttributeShortName = "Suppress";
	internal static readonly char[] Separator = new[] { ',' };

	/// <summary>
	/// Checks if a diagnostic should be suppressed for the given symbol based on custom attributes.
	/// </summary>
	public static bool ShouldSuppress(ISymbol symbol, string diagnosticId)
	{
		if (symbol == null)
		{
			return false;
		}

		// Check the symbol itself
		if (HasSuppressionAttribute(symbol, diagnosticId))
		{
			return true;
		}

		// Check containing type
		if (symbol.ContainingType != null && HasSuppressionAttribute(symbol.ContainingType, diagnosticId))
		{
			return true;
		}

		// Check containing namespace
		INamespaceSymbol? containingNamespace = symbol.ContainingNamespace;
		while (containingNamespace != null && !containingNamespace.IsGlobalNamespace)
		{
			if (HasSuppressionAttribute(containingNamespace, diagnosticId))
			{
				return true;
			}

			containingNamespace = containingNamespace.ContainingNamespace;
		}

		return false;
	}

	/// <summary>
	/// Checks if a diagnostic should be suppressed for the given syntax node based on custom attributes.
	/// </summary>
	public static bool ShouldSuppress(SyntaxNode node, SemanticModel semanticModel, string diagnosticId)
	{
		if (node == null || semanticModel == null)
		{
			return false;
		}

		// Try to get symbol for the node
		ISymbol? symbol = semanticModel.GetDeclaredSymbol(node);
		if (symbol != null && ShouldSuppress(symbol, diagnosticId))
		{
			return true;
		}

		// Check parent nodes
		SyntaxNode? parent = node.Parent;
		while (parent != null)
		{
			symbol = semanticModel.GetDeclaredSymbol(parent);
			if (symbol != null && ShouldSuppress(symbol, diagnosticId))
			{
				return true;
			}

			parent = parent.Parent;
		}

		return false;
	}

	private static bool HasSuppressionAttribute(ISymbol symbol, string diagnosticId)
	{
		if (symbol == null)
		{
			return false;
		}

		ImmutableArray<AttributeData> attributes = symbol.GetAttributes();
		foreach (AttributeData? attribute in attributes)
		{
			INamedTypeSymbol? attributeClass = attribute.AttributeClass;
			if (attributeClass == null)
			{
				continue;
			}

			string attributeName = attributeClass.Name;
			string fullAttributeName = attributeClass.ToDisplayString();
#pragma warning disable CA1310
			// Check for full name or short name (handle both "Suppress" and "SuppressAttribute")
			bool isMatch = fullAttributeName == SuppressAttributeName ||
						attributeName == SuppressAttributeShortName ||
						attributeName == SuppressAttributeShortName + "Attribute" ||
						fullAttributeName.EndsWith("." + SuppressAttributeShortName) ||
						fullAttributeName.EndsWith("." + SuppressAttributeShortName + "Attribute");
#pragma warning restore CA1310
			if (!isMatch)
			{
				continue;
			}

			// Get the diagnostic IDs from the attribute constructor argument
			if (attribute.ConstructorArguments.Length == 0)
			{
				continue;
			}

			TypedConstant diagnosticIdsArg = attribute.ConstructorArguments[0];
			if (diagnosticIdsArg.Kind != TypedConstantKind.Primitive &&
			diagnosticIdsArg.Kind != TypedConstantKind.Error)
			{
				continue;
			}

			string? diagnosticIdsString = diagnosticIdsArg.Value?.ToString();
			if (string.IsNullOrEmpty(diagnosticIdsString))
			{
				continue;
			}

			// Check if the diagnostic ID is in the list (comma-separated)
			var ids = diagnosticIdsString?.Split(Separator, StringSplitOptions.RemoveEmptyEntries)
				.Select(id => id.Trim())
				.Where(id => !string.IsNullOrEmpty(id)) ?? Enumerable.Empty<string>();

			if (ids.Contains(diagnosticId, StringComparer.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}
}
