using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;

namespace Aprbrown.Analyzers.CodeFixes;

/// <summary>
/// The <c>APB0004</c> code fix — moves an expression body's <c>=&gt;</c> to the end of the line
/// holding the token before it.
/// </summary>
/// <remarks>
/// Only trivia moves: everything that sat between the previous token and the arrow now follows the
/// arrow, so comments keep their place relative to the code and the expression keeps its
/// indentation. No fix is offered when a preprocessor directive lies in between, since moving the
/// arrow across it would change which conditional region the arrow belongs to.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ExpressionBodyArrowCodeFixProvider))]
[Shared]
public sealed class ExpressionBodyArrowCodeFixProvider : CodeFixProvider
{
    private const string Title = "Move '=>' to the end of the previous line";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; }
        = ImmutableArray.Create(Descriptors.ExpressionBodyArrow.Id);

    /// <summary>
    /// The batch fixer, which is safe here because every fix is a trivia edit confined to one
    /// document, so fixes for different members never touch the same tokens.
    /// </summary>
    /// <returns><see cref="WellKnownFixAllProviders.BatchFixer" />.</returns>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

        if (root is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            var arrow = root.FindToken(diagnostic.Location.SourceSpan.Start);

            if (!arrow.IsKind(SyntaxKind.EqualsGreaterThanToken)
                || TriviaBefore(arrow).Any(trivia => trivia.IsDirective))
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(MoveArrowUp(root, arrow))),
                    equivalenceKey: Title),
                diagnostic);
        }
    }

    private static SyntaxTriviaList TriviaBefore(SyntaxToken arrow) =>
        arrow.GetPreviousToken().TrailingTrivia.AddRange(arrow.LeadingTrivia);

    private static SyntaxNode MoveArrowUp(SyntaxNode root, SyntaxToken arrow)
    {
        var previous = arrow.GetPreviousToken();
        var before = WithoutWhitespaceBeforeLineBreak(TriviaBefore(arrow));
        var after = SyntaxFactory.TriviaList(
            arrow.TrailingTrivia.SkipWhile(trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia)));

        if (after.FirstOrDefault().IsKind(SyntaxKind.EndOfLineTrivia))
        {
            before = WithoutFinalLineBreak(before);
        }

        var movedTrivia = before.AddRange(after);

        return root.ReplaceTokens(
            [previous, arrow],
            (original, _) => original == previous
                ? previous.WithTrailingTrivia()
                : arrow.WithLeadingTrivia(SyntaxFactory.Space).WithTrailingTrivia(movedTrivia));
    }

    private static SyntaxTriviaList WithoutWhitespaceBeforeLineBreak(SyntaxTriviaList trivia) =>
        trivia.Count > 1
            && trivia[0].IsKind(SyntaxKind.WhitespaceTrivia)
            && trivia[1].IsKind(SyntaxKind.EndOfLineTrivia)
                ? trivia.RemoveAt(0)
                : trivia;

    private static SyntaxTriviaList WithoutFinalLineBreak(SyntaxTriviaList trivia)
    {
        var end = trivia.Count;

        while (end > 0 && trivia[end - 1].IsKind(SyntaxKind.WhitespaceTrivia))
        {
            end--;
        }

        if (end > 0 && trivia[end - 1].IsKind(SyntaxKind.EndOfLineTrivia))
        {
            end--;
        }

        return SyntaxFactory.TriviaList(trivia.Take(end));
    }
}
