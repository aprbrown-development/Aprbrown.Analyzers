using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Aprbrown.Analyzers.CodeFixes;

/// <summary>
/// The <c>APB0005</c> code fix — moves a trailing comment onto its own line directly above the line
/// it trailed, at that line's indentation.
/// </summary>
/// <remarks>
/// No fix is offered when the trailed line does not begin with a token of its own, such as the
/// closing line of a multi-line raw string literal, because there is no line start to hoist the
/// comment above.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(TrailingCommentCodeFixProvider))]
[Shared]
public sealed class TrailingCommentCodeFixProvider : CodeFixProvider
{
    private const string Title = "Move this comment onto its own line above the code";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; }
        = ImmutableArray.Create(Descriptors.TrailingComment.Id);

    /// <summary>
    /// The batch fixer, which merges every fix in a document into one edit. Each fix changes only
    /// the trivia of the tokens on its own line, so no two fixes overlap.
    /// </summary>
    /// <returns><see cref="WellKnownFixAllProviders.BatchFixer" />.</returns>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var text = await context.Document.GetTextAsync(context.CancellationToken).ConfigureAwait(false);

        if (root is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            var comment = root.FindTrivia(diagnostic.Location.SourceSpan.Start);

            if (LineStart(root, text, comment) is not var (firstToken, indentation))
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(Hoist(root, comment, firstToken, indentation))),
                    equivalenceKey: Title),
                diagnostic);
        }
    }

    /// <summary>
    /// The first token on the line <paramref name="comment" /> trails, and the whitespace that
    /// indents it, or <see langword="null" /> when the line does not begin with a token.
    /// </summary>
    private static (SyntaxToken FirstToken, string Indentation)? LineStart(
        SyntaxNode root,
        SourceText text,
        SyntaxTrivia comment)
    {
        var line = text.Lines.GetLineFromPosition(comment.SpanStart);
        var firstToken = root.FindToken(line.Start);

        if (firstToken.SpanStart < line.Start)
        {
            return null;
        }

        var indentation = text.ToString(TextSpan.FromBounds(line.Start, firstToken.SpanStart));

        return string.IsNullOrWhiteSpace(indentation) ? (firstToken, indentation) : null;
    }

    private static SyntaxNode Hoist(SyntaxNode root, SyntaxTrivia comment, SyntaxToken firstToken, string indentation)
    {
        var trailed = comment.Token;
        var hoisted = SyntaxFactory.TriviaList(comment, EndOfLine(root, trailed));

        if (indentation.Length > 0)
        {
            hoisted = hoisted.Add(SyntaxFactory.Whitespace(indentation));
        }

        return root.ReplaceTokens(
            new[] { trailed, firstToken },
            (original, _) =>
            {
                var token = original;

                if (original == trailed)
                {
                    token = token.WithTrailingTrivia(WithoutComment(token.TrailingTrivia, comment));
                }

                if (original == firstToken)
                {
                    token = token.WithLeadingTrivia(token.LeadingTrivia.AddRange(hoisted));
                }

                return token;
            });
    }

    private static SyntaxTriviaList WithoutComment(SyntaxTriviaList trivia, SyntaxTrivia comment)
    {
        var index = trivia.IndexOf(comment);
        var remaining = trivia.RemoveAt(index);

        return index > 0 && trivia[index - 1].IsKind(SyntaxKind.WhitespaceTrivia)
            ? remaining.RemoveAt(index - 1)
            : remaining;
    }

    /// <summary>
    /// The line break the trailed line ends with, so the hoisted line matches the document's
    /// line endings; the document's first line break when the comment ends the file.
    /// </summary>
    private static SyntaxTrivia EndOfLine(SyntaxNode root, SyntaxToken trailed) =>
        trailed.TrailingTrivia
            .Concat(root.DescendantTrivia())
            .FirstOrDefault(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia)) is { RawKind: not 0 } endOfLine
            ? endOfLine
            : SyntaxFactory.LineFeed;
}
