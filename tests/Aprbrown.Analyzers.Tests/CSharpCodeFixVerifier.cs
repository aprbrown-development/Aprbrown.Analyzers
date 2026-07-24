using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Aprbrown.Analyzers.Tests;

/// <summary>
/// Thin wrapper over <see cref="CSharpCodeFixTest{TAnalyzer, TCodeFix, TVerifier}"/>, pinned to the
/// same <c>Net80</c> reference-assembly set as <see cref="CSharpAnalyzerVerifier{TAnalyzer}"/>.
/// </summary>
/// <remarks>
/// The harness compiles the fixed source and fails on any diagnostic that was not declared
/// expected — compiler errors included. That is what turns "the fixed source compiles" from an
/// assumption into an assertion (spec §3.4): a fix that rewrote only the declaration token would
/// leave the body referring to a name that no longer exists, and <c>CS0103</c> would fail the test.
/// </remarks>
internal static class CSharpCodeFixVerifier<TAnalyzer, TCodeFix>
    where TAnalyzer : DiagnosticAnalyzer, new()
    where TCodeFix : CodeFixProvider, new()
{
    public static DiagnosticResult Diagnostic(string diagnosticId)
        => CSharpAnalyzerVerifier<TAnalyzer>.Diagnostic(diagnosticId);

    public static Task VerifyCodeFixAsync(
        string source,
        string fixedSource,
        params DiagnosticResult[] expected)
        => RunAsync(source, fixedSource, expectedTitle: null, expected);

    /// <summary>
    /// As <see cref="VerifyCodeFixAsync(string, string, DiagnosticResult[])"/>, and additionally
    /// asserts the title under which the fix is offered — the string a developer reads in the
    /// light bulb, which spec §3.4 fixes exactly.
    /// </summary>
    public static Task VerifyCodeFixAsync(
        string source,
        string fixedSource,
        string expectedTitle,
        params DiagnosticResult[] expected)
        => RunAsync(source, fixedSource, expectedTitle, expected);

    private static async Task RunAsync(
        string source,
        string fixedSource,
        string? expectedTitle,
        DiagnosticResult[] expected)
    {
        var test = new OfferedFixRecordingTest
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync(CancellationToken.None);

        if (expectedTitle is not null)
        {
            Assert.NotEmpty(test.OfferedTitles);
            Assert.All(test.OfferedTitles, title => Assert.Equal(expectedTitle, title));
        }
    }

    /// <summary>
    /// Records the title of every code action the provider registers. The testing library exposes
    /// no hook for asserting a title — only <c>CodeFixEquivalenceKey</c>, which would test the key
    /// rather than the text — so the registration callback is wrapped on its way in.
    /// </summary>
    private sealed class OfferedFixRecordingTest : CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>
    {
        public List<string> OfferedTitles { get; } = [];

        protected override CodeFixContext CreateCodeFixContext(
            Document document,
            TextSpan span,
            ImmutableArray<Diagnostic> diagnostics,
            Action<CodeAction, ImmutableArray<Diagnostic>> registerCodeFix,
            CancellationToken cancellationToken)
            => base.CreateCodeFixContext(
                document,
                span,
                diagnostics,
                (action, actionDiagnostics) =>
                {
                    OfferedTitles.Add(action.Title);
                    registerCodeFix(action, actionDiagnostics);
                },
                cancellationToken);
    }
}
