using System.Collections.Immutable;
using Companella.Analyzers.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Companella.Analyzers;

/// <summary>
/// Main analyzer for Companella code style and best practices.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class CompanellaAnalyzer : DiagnosticAnalyzer
{
	public const string DiagnosticId = "COMP001";
#pragma warning disable RS2008
	private static readonly DiagnosticDescriptor _rule = new(
		DiagnosticId,
		"Code style violation",
		"{0}",
		"Companella.CodeStyle",
		DiagnosticSeverity.Warning,
		true,
		"Enforces Companella coding standards.");
#pragma warning restore RS2008
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		ImmutableArray.Create(_rule);

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterSyntaxNodeAction(AnalyzeSyntax, SyntaxKind.ClassDeclaration);
		context.RegisterSyntaxNodeAction(AnalyzeSyntax, SyntaxKind.MethodDeclaration);
		context.RegisterSyntaxNodeAction(AnalyzeSyntax, SyntaxKind.PropertyDeclaration);
		context.RegisterSyntaxNodeAction(AnalyzeSyntax, SyntaxKind.FieldDeclaration);
	}

	private static void AnalyzeSyntax(SyntaxNodeAnalysisContext context)
	{
		switch (context.Node)
		{
			case ClassDeclarationSyntax classDecl:
				AnalyzeClass(context, classDecl);
				break;
			case MethodDeclarationSyntax methodDecl:
				AnalyzeMethod(context, methodDecl);
				break;
			case PropertyDeclarationSyntax propertyDecl:
				AnalyzeProperty(context, propertyDecl);
				break;
			case FieldDeclarationSyntax fieldDecl:
				AnalyzeField(context, fieldDecl);
				break;
		}
	}

	private static void AnalyzeClass(SyntaxNodeAnalysisContext context, ClassDeclarationSyntax classDecl)
	{
		INamedTypeSymbol? symbol = context.SemanticModel.GetDeclaredSymbol(classDecl);
		if (symbol == null)
		{
			return;
		}

		// Check if suppressed
		if (SuppressionHelper.ShouldSuppress(symbol, DiagnosticId))
		{
			return;
		}

		// Check naming convention: classes should be PascalCase
		if (!IsPascalCase(classDecl.Identifier.ValueText))
		{
			var diagnostic = Diagnostic.Create(
				_rule,
				classDecl.Identifier.GetLocation(),
				$"Class '{classDecl.Identifier.ValueText}' should use PascalCase naming convention.");
			context.ReportDiagnostic(diagnostic);
		}
	}

	// Method names that are exempt from PascalCase convention
	private static readonly HashSet<string> _exemptMethodNames = new(StringComparer.Ordinal)
	{
		"load"
	};

	private static void AnalyzeMethod(SyntaxNodeAnalysisContext context, MethodDeclarationSyntax methodDecl)
	{
		IMethodSymbol? symbol = context.SemanticModel.GetDeclaredSymbol(methodDecl);
		if (symbol == null)
		{
			return;
		}

		// Check if suppressed
		if (SuppressionHelper.ShouldSuppress(symbol, DiagnosticId))
		{
			return;
		}

		string methodName = methodDecl.Identifier.ValueText;

		// Check for exempt method names
		if (_exemptMethodNames.Contains(methodName))
		{
			return;
		}

		// Check naming convention: methods should be PascalCase
		if (!IsPascalCase(methodName))
		{
			var diagnostic = Diagnostic.Create(
				_rule,
				methodDecl.Identifier.GetLocation(),
				$"Method '{methodName}' should use PascalCase naming convention.");
			context.ReportDiagnostic(diagnostic);
		}
	}

	private static void AnalyzeProperty(SyntaxNodeAnalysisContext context, PropertyDeclarationSyntax propertyDecl)
	{
		IPropertySymbol? symbol = context.SemanticModel.GetDeclaredSymbol(propertyDecl);
		if (symbol == null)
		{
			return;
		}

		// Check if suppressed
		if (SuppressionHelper.ShouldSuppress(symbol, DiagnosticId))
		{
			return;
		}

		// Check naming convention: properties should be PascalCase
		if (!IsPascalCase(propertyDecl.Identifier.ValueText))
		{
			var diagnostic = Diagnostic.Create(
				_rule,
				propertyDecl.Identifier.GetLocation(),
				$"Property '{propertyDecl.Identifier.ValueText}' should use PascalCase naming convention.");
			context.ReportDiagnostic(diagnostic);
		}
	}

	private static void AnalyzeField(SyntaxNodeAnalysisContext context, FieldDeclarationSyntax fieldDecl)
	{
		foreach (VariableDeclaratorSyntax variable in fieldDecl.Declaration.Variables)
		{
			var symbol = context.SemanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;
			if (symbol == null)
			{
				continue;
			}

			// Check if suppressed
			if (SuppressionHelper.ShouldSuppress(symbol, DiagnosticId))
			{
				continue;
			}

			string fieldName = variable.Identifier.ValueText;
			bool isConstant = symbol.IsConst;
			bool isStaticReadonly = symbol.IsStatic && symbol.IsReadOnly;
			bool isInWinApiContext = WinApiContextHelper.IsInWinApiContext(symbol);

			// Constants and static readonly fields in WinApiContext should use SCREAMING_SNAKE_CASE
			if ((isConstant || isStaticReadonly) && isInWinApiContext)
			{
				if (!WinApiContextHelper.IsScreamingSnakeCase(fieldName))
				{
					var diagnostic = Diagnostic.Create(
						_rule,
						variable.Identifier.GetLocation(),
						$"Win32 API constant '{fieldName}' should use SCREAMING_SNAKE_CASE naming convention (e.g., WM_HOTKEY, SWP_NOMOVE).");
					context.ReportDiagnostic(diagnostic);
				}

				continue; // Skip other checks for WinApi constants
			}

			// Private fields should use _camelCase
			if (symbol.DeclaredAccessibility == Accessibility.Private)
			{
#pragma warning disable CA1310
				if (!fieldName.StartsWith("_") || !IsCamelCase(fieldName.Substring(1)))
				{
					var diagnostic = Diagnostic.Create(
						_rule,
						variable.Identifier.GetLocation(),
						$"Private field '{fieldName}' should use _camelCase naming convention.");
					context.ReportDiagnostic(diagnostic);
				}
#pragma warning restore CA1310
			}
			// Public/internal fields and constants should use PascalCase
			else
			{
				if (!IsPascalCase(fieldName))
				{
					string expectedConvention = isConstant ? "PascalCase" : "PascalCase";
					var diagnostic = Diagnostic.Create(
						_rule,
						variable.Identifier.GetLocation(),
						$"Public field '{fieldName}' should use {expectedConvention} naming convention.");
					context.ReportDiagnostic(diagnostic);
				}
			}
		}
	}

	private static bool IsPascalCase(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return false;
		}

		// Must start with uppercase letter
		if (!char.IsUpper(name[0]))
		{
			return false;
		}

		// Rest can be letters, digits, or underscores
		// Allow multiple consecutive uppercase (for acronyms like XMLParser)
		for (int i = 1; i < name.Length; i++)
		{
			if (!char.IsLetterOrDigit(name[i]) && name[i] != '_')
			{
				return false;
			}
		}

		return true;
	}

	private static bool IsCamelCase(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return false;
		}

		// Must start with lowercase letter
		if (!char.IsLower(name[0]))
		{
			return false;
		}

		// Rest can be letters, digits, or underscores
		for (int i = 1; i < name.Length; i++)
		{
			if (!char.IsLetterOrDigit(name[i]) && name[i] != '_')
			{
				return false;
			}
		}

		return true;
	}
}
