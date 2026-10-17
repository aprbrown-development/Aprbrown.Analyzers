using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aprbrown.Analyzers;

/// <summary>
/// APB0004 — flags an expression body whose <c>=&gt;</c> starts a line instead of ending the
/// declaration line before it. Reported on the arrow.
/// </summary>
/// <remarks>
/// Registered on <see cref="SyntaxKind.ArrowExpressionClause"/> only, which is the expression body
/// of a method, constructor, finalizer, operator, property, indexer, accessor or local function. A
/// lambda's arrow and a switch-expression arm's arrow belong to other node kinds and never reach
/// the callback.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExpressionBodyArrowAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
        = ImmutableArray.Create(Descriptors.ExpressionBodyArrow);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeArrowExpressionClause, SyntaxKind.ArrowExpressionClause);
    }

    private static void AnalyzeArrowExpressionClause(SyntaxNodeAnalysisContext context)
    {
        var arrow = ((ArrowExpressionClauseSyntax)context.Node).ArrowToken;

        if (!StartsItsLine(context.Node.SyntaxTree, arrow, context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Descriptors.ExpressionBodyArrow, arrow.GetLocation()));
    }

    private static bool StartsItsLine(SyntaxTree tree, SyntaxToken token, CancellationToken cancellationToken)
    {
        var previousLine = tree.GetLineSpan(token.GetPreviousToken().Span, cancellationToken).EndLinePosition.Line;
        var tokenLine = tree.GetLineSpan(token.Span, cancellationToken).StartLinePosition.Line;

        return tokenLine > previousLine;
    }
}
