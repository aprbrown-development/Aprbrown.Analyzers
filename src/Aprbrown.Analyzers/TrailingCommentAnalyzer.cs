using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aprbrown.Analyzers;

/// <summary>
/// APB0005 — flags a <c>//</c> comment that shares its line with code before it.
/// </summary>
/// <remarks>
/// Block comments and documentation are other trivia kinds, and a comment on a preprocessor
/// directive lives inside the directive's structured trivia, which the walk does not enter, so none
/// of them is ever a candidate. A comment that is a tool directive is exempt, because the tool reads
/// it from the line it trails.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TrailingCommentAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableArray<string> ToolDirectivePrefixes =
        ImmutableArray.Create("NOSONAR", "ReSharper ", "lang=", "language=");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
        = ImmutableArray.Create(Descriptors.TrailingComment);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(AnalyzeTree);
    }

    private static void AnalyzeTree(SyntaxTreeAnalysisContext context)
    {
        var root = context.Tree.GetRoot(context.CancellationToken);

        foreach (var trivia in root.DescendantTrivia())
        {
            if (IsTrailingLineComment(trivia) && !IsToolDirective(trivia))
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.TrailingComment, trivia.GetLocation()));
            }
        }
    }

    private static bool IsTrailingLineComment(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) && trivia.SpanStart >= trivia.Token.Span.End;

    private static bool IsToolDirective(SyntaxTrivia comment)
    {
        var text = comment.ToString().Substring("//".Length).TrimStart();

        return ToolDirectivePrefixes.Any(prefix => text.StartsWith(prefix, StringComparison.Ordinal));
    }
}
