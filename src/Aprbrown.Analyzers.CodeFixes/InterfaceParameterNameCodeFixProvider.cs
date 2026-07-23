using System;
using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;

namespace Aprbrown.Analyzers.CodeFixes;

/// <summary>
/// The <c>APB0003</c> code fix (spec §3.4) — renames an implementing parameter to the name the
/// interface gives it.
/// </summary>
/// <remarks>
/// The fix renames the <em>symbol</em>, not the declaration token. A parameter's name appears
/// throughout the method body, so rewriting the declaration alone would produce code that does not
/// compile; <see cref="Renamer"/> updates every reference and resolves the conflicts a plain
/// textual substitution would create. That is why the fix returns a changed solution rather than a
/// changed document, and why this assembly needs <c>Microsoft.CodeAnalysis.CSharp.Workspaces</c> —
/// the fix is trivial to describe and not at all trivial to implement (spec §10, correction 4).
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(InterfaceParameterNameCodeFixProvider))]
[Shared]
public sealed class InterfaceParameterNameCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; }
        = ImmutableArray.Create(DiagnosticIds.InterfaceParameterName);

    /// <summary>
    /// No batch fix-all support, deliberately (spec §3.4). Every fix-all implementation available
    /// here computes its changes against one solution snapshot and applies them together; two
    /// renames in a single document derived that way conflict or clobber each other. Single
    /// invocation only in v1, so "Fix all occurrences" never appears in the light bulb.
    /// </summary>
    /// <returns>Always <see langword="null" />.</returns>
    public override FixAllProvider? GetFixAllProvider() => null;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);

        if (root is null || semanticModel is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            // The analyzer reports on the parameter's identifier, so the enclosing declaration is
            // one step up; FirstAncestorOrSelf absorbs the difference between Roslyn handing back
            // the identifier's parent and the parameter itself.
            if (root.FindNode(diagnostic.Location.SourceSpan)
                    ?.FirstAncestorOrSelf<ParameterSyntax>() is not { } parameter)
            {
                continue;
            }

            // The name to rename to is the analyzer's conclusion, carried in the property bag
            // rather than re-derived here. Absent or empty means a diagnostic this fixer does not
            // understand — from an older analyzer version, say — and silence beats a bad rename.
            if (diagnostic.Properties.TryGetValue(DiagnosticProperties.ExpectedName, out var expected) is false
                || string.IsNullOrEmpty(expected))
            {
                continue;
            }

            // Renaming into a name the member already declares does not resolve the collision —
            // Roslyn marks it as a rename conflict and hands back source that does not compile.
            // A fix that breaks the build is worse than no fix, so the diagnostic is left for the
            // developer to settle by hand.
            if (NameIsAlreadyDeclared(semanticModel, parameter, expected!, context.CancellationToken))
            {
                continue;
            }

            var title = $"Rename parameter to '{expected}'";

            context.RegisterCodeFix(
                CodeAction.Create(
                    title,
                    cancellationToken => RenameAsync(context.Document, parameter, expected!, cancellationToken),
                    equivalenceKey: title),
                diagnostic);
        }
    }

    /// <summary>
    /// Whether the member declaring <paramref name="parameter" /> already declares
    /// <paramref name="name" /> in a scope that would collide with it — another parameter, a
    /// local, a pattern designation, a local function.
    /// </summary>
    /// <remarks>
    /// Nearly the whole member is searched rather than only the scopes visible from the parameter,
    /// because C# forbids a nested declaration from shadowing an enclosing one: a local in a block
    /// halfway down the method collides with the parameter just as squarely as one on the first
    /// line. The exception is a <see langword="static" /> local function or lambda, which captures
    /// nothing and so is allowed to shadow — see <see cref="OpensAnIndependentScope" />.
    /// </remarks>
    private static bool NameIsAlreadyDeclared(
        SemanticModel semanticModel,
        ParameterSyntax parameter,
        string name,
        CancellationToken cancellationToken)
    {
        // ParameterSyntax -> ParameterListSyntax -> the declaring member.
        if (parameter.Parent?.Parent is not { } member)
        {
            return false;
        }

        foreach (var node in member.DescendantNodes(descendIntoChildren: node => !OpensAnIndependentScope(node)))
        {
            if (semanticModel.GetDeclaredSymbol(node, cancellationToken) is { } declared
                && string.Equals(declared.Name, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="node" /> opens a scope that may legally reuse an enclosing name.
    /// </summary>
    /// <remarks>
    /// A <see langword="static" /> local function (C# 8) or static lambda (C# 9) captures nothing
    /// from its enclosing method, and the language lets it declare names the method already uses.
    /// Their non-static counterparts do capture, and shadowing there is <c>CS0136</c>. Treating
    /// both alike would withdraw the fix from a rename that is perfectly legal.
    /// </remarks>
    private static bool OpensAnIndependentScope(SyntaxNode node) => node switch
    {
        LocalFunctionStatementSyntax localFunction => localFunction.Modifiers.Any(SyntaxKind.StaticKeyword),
        AnonymousFunctionExpressionSyntax lambda => lambda.Modifiers.Any(SyntaxKind.StaticKeyword),
        _ => false,
    };

    private static async Task<Solution> RenameAsync(
        Document document,
        ParameterSyntax parameter,
        string newName,
        CancellationToken cancellationToken)
    {
        var solution = document.Project.Solution;
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

        // A fix computed against one snapshot can be applied against a later one. Returning the
        // original solution is how a code fix says "nothing to do" — anything else would be a
        // change built on a symbol that no longer exists.
        if (semanticModel?.GetDeclaredSymbol(parameter, cancellationToken) is not IParameterSymbol symbol)
        {
            return solution;
        }

        return await Renamer
            .RenameSymbolAsync(solution, symbol, default(SymbolRenameOptions), newName, cancellationToken)
            .ConfigureAwait(false);
    }
}
