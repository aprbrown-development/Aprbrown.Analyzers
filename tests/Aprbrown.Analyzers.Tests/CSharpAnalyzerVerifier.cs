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
    public static DiagnosticResult Diagnostic(string diagnosticId)
        => new(diagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);

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
