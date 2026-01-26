using System.Collections.Immutable;
using Companella.Analyzers.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Companella.Analyzers;

/// <summary>
/// Analyzer for accessibility modifier rules from .editorconfig.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AccessibilityModifierAnalyzer : DiagnosticAnalyzer
{
	public const string DiagnosticId = "COMP004";
#pragma warning disable RS2008
	private static readonly DiagnosticDescriptor _rule = new(
		DiagnosticId,
		"Missing accessibility modifier",
		"Non-interface members should have explicit accessibility modifiers",
		"Companella.CodeStyle",
		DiagnosticSeverity.Warning,
		true,
		"Enforces explicit accessibility modifiers for non-interface members (per .editorconfig).");
#pragma warning restore RS2008
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		ImmutableArray.Create(_rule);

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterSyntaxNodeAction(AnalyzeClass, SyntaxKind.ClassDeclaration);
		context.RegisterSyntaxNodeAction(AnalyzeStruct, SyntaxKind.StructDeclaration);
		context.RegisterSyntaxNodeAction(AnalyzeEnum, SyntaxKind.EnumDeclaration);
		context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
		context.RegisterSyntaxNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
		context.RegisterSyntaxNodeAction(AnalyzeField, SyntaxKind.FieldDeclaration);
		context.RegisterSyntaxNodeAction(AnalyzeEvent, SyntaxKind.EventDeclaration);
	}

	private static void AnalyzeClass(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not ClassDeclarationSyntax classDecl)
		{
			return;
		}

		INamedTypeSymbol? symbol = context.SemanticModel.GetDeclaredSymbol(classDecl);
		if (symbol == null)
		{
			return;
		}

		// Interfaces are exempt
		if (symbol.TypeKind == TypeKind.Interface)
		{
			return;
		}

		// Check if suppressed
		if (SuppressionHelper.ShouldSuppress(symbol, DiagnosticId))
		{
			return;
		}

		// Check if accessibility modifier is present
		if (!HasAccessibilityModifier(classDecl.Modifiers))
		{
			var diagnostic = Diagnostic.Create(
				_rule,
				classDecl.Identifier.GetLocation(),
				$"Class '{classDecl.Identifier.ValueText}' should have an explicit accessibility modifier.");
			context.ReportDiagnostic(diagnostic);
		}
	}

	private static void AnalyzeStruct(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not StructDeclarationSyntax structDecl)
		{
			return;
		}

		INamedTypeSymbol? symbol = context.SemanticModel.GetDeclaredSymbol(structDecl);
		if (symbol == null)
		{
			return;
		}

		// Check if suppressed
		if (SuppressionHelper.ShouldSuppress(symbol, DiagnosticId))
		{
			return;
		}

		// Check if accessibility modifier is present
		if (!HasAccessibilityModifier(structDecl.Modifiers))
		{
			var diagnostic = Diagnostic.Create(
				_rule,
				structDecl.Identifier.GetLocation(),
				$"Struct '{structDecl.Identifier.ValueText}' should have an explicit accessibility modifier.");
			context.ReportDiagnostic(diagnostic);
		}
	}

	private static void AnalyzeEnum(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not EnumDeclarationSyntax enumDecl)
		{
			return;
		}

		INamedTypeSymbol? symbol = context.SemanticModel.GetDeclaredSymbol(enumDecl);
		if (symbol == null)
		{
			return;
		}

		// Check if suppressed
		if (SuppressionHelper.ShouldSuppress(symbol, DiagnosticId))
		{
			return;
		}

		// Check if accessibility modifier is present
		if (!HasAccessibilityModifier(enumDecl.Modifiers))
		{
			var diagnostic = Diagnostic.Create(
				_rule,
				enumDecl.Identifier.GetLocation(),
				$"Enum '{enumDecl.Identifier.ValueText}' should have an explicit accessibility modifier.");
			context.ReportDiagnostic(diagnostic);
		}
	}

	private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not MethodDeclarationSyntax methodDecl)
		{
			return;
		}

		IMethodSymbol? symbol = context.SemanticModel.GetDeclaredSymbol(methodDecl);
		if (symbol == null)
		{
			return;
		}

		// Interface methods are exempt
		if (symbol.ContainingType?.TypeKind == TypeKind.Interface)
		{
			return;
		}

		// Check if suppressed
		if (SuppressionHelper.ShouldSuppress(symbol, DiagnosticId))
		{
			return;
		}

		// Check if accessibility modifier is present
		if (!HasAccessibilityModifier(methodDecl.Modifiers))
		{
			var diagnostic = Diagnostic.Create(
				_rule,
				methodDecl.Identifier.GetLocation(),
				$"Method '{methodDecl.Identifier.ValueText}' should have an explicit accessibility modifier.");
			context.ReportDiagnostic(diagnostic);
		}
	}

	private static void AnalyzeProperty(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not PropertyDeclarationSyntax propertyDecl)
		{
			return;
		}

		IPropertySymbol? symbol = context.SemanticModel.GetDeclaredSymbol(propertyDecl);
		if (symbol == null)
		{
			return;
		}

		// Interface properties are exempt
		if (symbol.ContainingType?.TypeKind == TypeKind.Interface)
		{
			return;
		}

		// Check if suppressed
		if (SuppressionHelper.ShouldSuppress(symbol, DiagnosticId))
		{
			return;
		}

		// Check if accessibility modifier is present
		if (!HasAccessibilityModifier(propertyDecl.Modifiers))
		{
			var diagnostic = Diagnostic.Create(
				_rule,
				propertyDecl.Identifier.GetLocation(),
				$"Property '{propertyDecl.Identifier.ValueText}' should have an explicit accessibility modifier.");
			context.ReportDiagnostic(diagnostic);
		}
	}

	private static void AnalyzeField(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not FieldDeclarationSyntax fieldDecl)
		{
			return;
		}

		foreach (VariableDeclaratorSyntax variable in fieldDecl.Declaration.Variables)
		{
			var symbol = context.SemanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;
			if (symbol == null)
			{
				continue;
			}

			// Interface fields are exempt
			if (symbol.ContainingType?.TypeKind == TypeKind.Interface)
			{
				continue;
			}

			// Check if suppressed
			if (SuppressionHelper.ShouldSuppress(symbol, DiagnosticId))
			{
				continue;
			}

			// Check if accessibility modifier is present
			if (!HasAccessibilityModifier(fieldDecl.Modifiers))
			{
				var diagnostic = Diagnostic.Create(
					_rule,
					variable.Identifier.GetLocation(),
					$"Field '{variable.Identifier.ValueText}' should have an explicit accessibility modifier.");
				context.ReportDiagnostic(diagnostic);
			}
		}
	}

	private static void AnalyzeEvent(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not EventDeclarationSyntax eventDecl)
		{
			return;
		}

		IEventSymbol? symbol = context.SemanticModel.GetDeclaredSymbol(eventDecl);
		if (symbol == null)
		{
			return;
		}

		// Interface events are exempt
		if (symbol.ContainingType?.TypeKind == TypeKind.Interface)
		{
			return;
		}

		// Check if suppressed
		if (SuppressionHelper.ShouldSuppress(symbol, DiagnosticId))
		{
			return;
		}

		// Check if accessibility modifier is present
		if (!HasAccessibilityModifier(eventDecl.Modifiers))
		{
			var diagnostic = Diagnostic.Create(
				_rule,
				eventDecl.Identifier.GetLocation(),
				$"Event '{eventDecl.Identifier.ValueText}' should have an explicit accessibility modifier.");
			context.ReportDiagnostic(diagnostic);
		}
	}

	private static bool HasAccessibilityModifier(SyntaxTokenList modifiers)
	{
		bool hasPrivate = false;
		bool hasProtected = false;
		bool hasInternal = false;
		bool hasPublic = false;

		foreach (SyntaxToken modifier in modifiers)
		{
			if (modifier.IsKind(SyntaxKind.PublicKeyword))
			{
				hasPublic = true;
			}
			else if (modifier.IsKind(SyntaxKind.PrivateKeyword))
			{
				hasPrivate = true;
			}
			else if (modifier.IsKind(SyntaxKind.ProtectedKeyword))
			{
				hasProtected = true;
			}
			else if (modifier.IsKind(SyntaxKind.InternalKeyword))
			{
				hasInternal = true;
			}
		}

		// Check for any accessibility modifier (public, private, protected, internal, or combinations)
		return hasPublic || hasPrivate || hasProtected || hasInternal;
	}
}
