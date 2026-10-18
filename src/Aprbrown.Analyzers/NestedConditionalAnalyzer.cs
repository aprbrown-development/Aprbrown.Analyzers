using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aprbrown.Analyzers;

/// <summary>
/// APB0006 — flags a conditional expression nested anywhere inside another conditional expression's
/// condition or branches, reported at the inner conditional.
/// </summary>
/// <remarks>
/// <para>
/// Every expression between the two is looked through, so a conditional wrapped in parentheses, a
/// cast, an argument or a switch expression is still nested. A lambda, anonymous method or local
/// function body is a scope of its own: nesting inside it is judged afresh, and a conditional
/// enclosing the function does not count. A local function is a statement, so reaching any enclosing
/// statement or member ends the search the same way.
/// </para>
/// <para>
/// <c>?.</c>, <c>??</c>, <c>??=</c> and switch expressions are other syntax kinds, so none is
/// reported and none counts as the outer conditional.
/// </para>
/// </remarks>
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
            .TakeWhile(ancestor => !EndsNestingSearch(ancestor))
            .Any(ancestor => ancestor is ConditionalExpressionSyntax);

    private static bool EndsNestingSearch(SyntaxNode node) =>
        node is AnonymousFunctionExpressionSyntax or StatementSyntax or MemberDeclarationSyntax;
}
