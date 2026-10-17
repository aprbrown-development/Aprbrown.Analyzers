using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Aprbrown.Analyzers.Tests;

/// <summary>
/// Thin wrapper over <see cref="CSharpAnalyzerTest{TAnalyzer, TVerifier}"/> that pins a modern
/// reference-assembly set — <c>Net80</c>, whose default C# 12 language version parses both records
/// and class primary constructors, which the required-test snippets need.
/// </summary>
internal static class CSharpAnalyzerVerifier<TAnalyzer>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    // At the severity the analyzer's own descriptor declares, rather than a copied literal; the
    // descriptor contract test holds that default to the shipped config. An ID the analyzer does not
    // support fails here, at Single.
    public static DiagnosticResult Diagnostic(string diagnosticId) =>
        new(
            diagnosticId,
            new TAnalyzer().SupportedDiagnostics
                .Single(d => string.Equals(d.Id, diagnosticId, StringComparison.Ordinal))
                .DefaultSeverity);

    public static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync(CancellationToken.None);
    }
}
