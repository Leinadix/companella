using System.Collections.Immutable;
using Companella.Analyzers.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Companella.Analyzers;

/// <summary>
/// Analyzer for namespace-related rules from .editorconfig.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class NamespaceAnalyzer : DiagnosticAnalyzer
{
	public const string DiagnosticIdFileScopedNamespace = "COMP002";
	public const string DiagnosticIdUsingPlacement = "COMP003";
#pragma warning disable RS2008
	private static readonly DiagnosticDescriptor _fileScopedNamespaceRule = new(
		DiagnosticIdFileScopedNamespace,
		"Use file-scoped namespace",
		"Namespace should use file-scoped namespace declaration",
		"Companella.CodeStyle",
		DiagnosticSeverity.Warning,
		true,
		"Enforces file-scoped namespace declarations (per CONTRIBUTING.md).");

	private static readonly DiagnosticDescriptor _usingPlacementRule = new(
		DiagnosticIdUsingPlacement,
		"Using directive placement",
		"Using directives should be placed outside namespace declaration",
		"Companella.CodeStyle",
		DiagnosticSeverity.Warning,
		true,
		"Enforces using directives outside namespace (per .editorconfig).");
#pragma warning restore RS2008
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		ImmutableArray.Create(_fileScopedNamespaceRule, _usingPlacementRule);

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterSyntaxNodeAction(AnalyzeNamespace, SyntaxKind.NamespaceDeclaration);
		context.RegisterSyntaxNodeAction(AnalyzeUsingDirective, SyntaxKind.UsingDirective);
	}

	private static void AnalyzeNamespace(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not NamespaceDeclarationSyntax namespaceDecl)
		{
			return;
		}

		INamespaceSymbol? symbol = context.SemanticModel.GetDeclaredSymbol(namespaceDecl);
		if (symbol == null)
		{
			return;
		}

		// Check if suppressed
		if (SuppressionHelper.ShouldSuppress(symbol, DiagnosticIdFileScopedNamespace))
		{
			return;
		}

		// Check if it's a file-scoped namespace (namespace X;)
		// Block-scoped namespaces (namespace X { }) should be reported
		var diagnostic = Diagnostic.Create(
			_fileScopedNamespaceRule,
			namespaceDecl.NamespaceKeyword.GetLocation(),
			"Use file-scoped namespace declaration: 'namespace X;' instead of 'namespace X { }'.");
		context.ReportDiagnostic(diagnostic);
	}

	private static void AnalyzeUsingDirective(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not UsingDirectiveSyntax usingDirective)
		{
			return;
		}

		// Check if suppressed
		if (SuppressionHelper.ShouldSuppress(usingDirective, context.SemanticModel, DiagnosticIdUsingPlacement))
		{
			return;
		}

		// Check if using directive is inside a namespace
		SyntaxNode? parent = usingDirective.Parent;
		while (parent != null)
		{
			if (parent is NamespaceDeclarationSyntax)
			{
				// Using directive is inside namespace - this violates the rule
				var diagnostic = Diagnostic.Create(
					_usingPlacementRule,
					usingDirective.GetLocation(),
					"Using directives should be placed outside namespace declaration.");
				context.ReportDiagnostic(diagnostic);
				return;
			}

			if (parent is CompilationUnitSyntax)
			{
				return; // Reached compilation unit, using is at top level - OK
			}

			parent = parent.Parent;
		}
	}
}
