using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Companella.Analyzers.Helpers;

/// <summary>
/// Helper class for checking if code is within a WinApiContext.
/// </summary>
internal static class WinApiContextHelper
{
	private const string WinApiContextAttributeName = "Companella.Analyzers.Attributes.WinApiContextAttribute";
	private const string WinApiContextAttributeShortName = "WinApiContext";

	/// <summary>
	/// Checks if a symbol is within a WinApiContext (has the attribute or is within a type/namespace that has it).
	/// </summary>
	public static bool IsInWinApiContext(ISymbol symbol)
	{
		if (symbol == null)
		{
			return false;
		}

		// Check the symbol itself
		if (HasWinApiContextAttribute(symbol))
		{
			return true;
		}

		// Check containing type
		if (symbol.ContainingType != null && IsInWinApiContext(symbol.ContainingType))
		{
			return true;
		}

		// Check containing namespace
		INamespaceSymbol? containingNamespace = symbol.ContainingNamespace;
		while (containingNamespace != null && !containingNamespace.IsGlobalNamespace)
		{
			if (HasWinApiContextAttribute(containingNamespace))
			{
				return true;
			}

			containingNamespace = containingNamespace.ContainingNamespace;
		}

		return false;
	}

	/// <summary>
	/// Checks if a symbol has the WinApiContext attribute.
	/// </summary>
	private static bool HasWinApiContextAttribute(ISymbol symbol)
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
			// Check for full name or short name
			if (fullAttributeName == WinApiContextAttributeName ||
			attributeName == WinApiContextAttributeShortName ||
			fullAttributeName.EndsWith("." + WinApiContextAttributeShortName))
#pragma warning restore CA1310
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Checks if a constant field name follows Windows API naming conventions (SCREAMING_SNAKE_CASE).
	/// </summary>
	public static bool IsScreamingSnakeCase(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return false;
		}

		// Must contain at least one underscore
		if (!name.Contains('_'))
		{
			return false;
		}

		// All characters must be uppercase letters, digits, or underscores
		// Must start with a letter or underscore
		if (!char.IsUpper(name[0]) && name[0] != '_')
		{
			return false;
		}

		for (int i = 1; i < name.Length; i++)
		{
			if (!char.IsUpper(name[i]) && !char.IsDigit(name[i]) && name[i] != '_')
			{
				return false;
			}
		}

		return true;
	}
}
