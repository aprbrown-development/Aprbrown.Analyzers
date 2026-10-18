using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aprbrown.Analyzers;

/// <summary>
/// APB0006 — flags a conditional expression nested inside another conditional expression.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NestedConditionalAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
        = ImmutableArray.Create(Descriptors.NestedConditional);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeConditional, SyntaxKind.ConditionalExpression);
    }

    private static void AnalyzeConditional(SyntaxNodeAnalysisContext context)
    {
        if (IsNestedInConditional(context.Node))
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.NestedConditional, context.Node.GetLocation()));
        }
    }

    private static bool IsNestedInConditional(SyntaxNode conditional) =>
        conditional.Ancestors()
            .TakeWhile(ancestor => !IsScopeBoundary(ancestor))
            .Any(ancestor => ancestor is ConditionalExpressionSyntax);

    // An enclosing statement or member means the walk has left the expression the conditional sits
    // in. A local function is itself a statement, so it ends the walk the same way.
    private static bool IsScopeBoundary(SyntaxNode node) =>
        node is AnonymousFunctionExpressionSyntax or StatementSyntax or MemberDeclarationSyntax;
}
