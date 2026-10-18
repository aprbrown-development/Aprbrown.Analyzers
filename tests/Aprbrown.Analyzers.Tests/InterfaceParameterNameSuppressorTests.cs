using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Aprbrown.Analyzers.Tests;

/// <summary>
/// Which <c>CA1725</c> diagnostics <see cref="InterfaceParameterNameSuppressor"/> suppresses. A stub
/// stands in for <c>CA1725</c> and reports on every parameter of type <c>C</c>, so each test reads as
/// the suppressor's verdict on the parameters <c>C</c> declares.
/// </summary>
public sealed class InterfaceParameterNameSuppressorTests
{
    [Fact]
    public async Task Implicit_implementation_mismatch_is_suppressed()
    {
        const string source = """
            interface I { void M(int alpha); }
            class C : I { public void M(int beta) { } }
            """;

        Assert.True(await IsSuppressedAsync(source, "beta"));
    }

    [Fact]
    public async Task Explicit_implementation_mismatch_is_suppressed()
    {
        const string source = """
            interface I { void M(int alpha); }
            class C : I { void I.M(int beta) { } }
            """;

        Assert.True(await IsSuppressedAsync(source, "beta"));
    }

    [Fact]
    public async Task Override_mismatch_is_not_suppressed()
    {
        const string source = """
            abstract class B { public abstract void M(int alpha); }
            class C : B { public override void M(int beta) { } }
            """;

        Assert.False(await IsSuppressedAsync(source, "beta"));
    }

    [Fact]
    public async Task Override_mismatched_against_its_base_is_not_suppressed_when_it_also_implements_an_interface()
    {
        const string source = """
            interface I { void M(int beta); }
            abstract class B { public abstract void M(int alpha); }
            class C : B, I { public override void M(int beta) { } }
            """;

        Assert.False(await IsSuppressedAsync(source, "beta"));
    }

    [Fact]
    public async Task Override_matching_its_base_is_suppressed_when_it_also_implements_an_interface()
    {
        const string source = """
            interface I { void M(int gamma); }
            abstract class B { public abstract void M(int beta); }
            class C : B, I { public override void M(int beta) { } }
            """;

        Assert.True(await IsSuppressedAsync(source, "beta"));
    }

    [Fact]
    public async Task Method_implementing_two_interfaces_under_other_names_is_suppressed()
    {
        const string source = """
            interface IA { void Z(int a); }
            interface IB { void Z(int b); }
            class C : IA, IB { public void Z(int c) { } }
            """;

        Assert.True(await IsSuppressedAsync(source, "c"));
    }

    [Fact]
    public async Task Another_diagnostic_on_a_suppressed_parameter_is_untouched()
    {
        const string source = """
            interface I { void M(int alpha); }
            class C : I { public void M(int beta) { } }
            """;

        Assert.False(await IsSuppressedAsync(source, "beta", EveryParameterAnalyzer.Other));
    }

    [Fact]
    public async Task Method_that_neither_overrides_nor_implements_is_not_suppressed()
    {
        const string source = """
            class C { public void M(int beta) { } }
            """;

        Assert.False(await IsSuppressedAsync(source, "beta"));
    }

    private static async Task<bool> IsSuppressedAsync(string source, string parameter, string id = EveryParameterAnalyzer.CA1725) =>
        (await ReportedAsync(source)).Single(d => string.Equals(d.Parameter, parameter, StringComparison.Ordinal) && string.Equals(d.Id, id, StringComparison.Ordinal)).IsSuppressed;

    private static async Task<ImmutableArray<(string Parameter, string Id, bool IsSuppressed)>> ReportedAsync(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, cancellationToken: CancellationToken.None);
        var references = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var compilation = CSharpCompilation.Create(
            "Snippet",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new EveryParameterAnalyzer(), new InterfaceParameterNameSuppressor());
        var options = new CompilationWithAnalyzersOptions(
            new AnalyzerOptions([]),
            onAnalyzerException: null,
            concurrentAnalysis: false,
            logAnalyzerExecutionTime: false,
            reportSuppressedDiagnostics: true);
        var diagnostics = await compilation.WithAnalyzers(analyzers, options).GetAnalyzerDiagnosticsAsync(CancellationToken.None);

        return [.. diagnostics.Select(d => (tree.ToString()[d.Location.SourceSpan.Start..d.Location.SourceSpan.End], d.Id, d.IsSuppressed))];
    }

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class EveryParameterAnalyzer : DiagnosticAnalyzer
    {
        public const string CA1725 = nameof(CA1725);

        public const string Other = "OTHER0001";

        private static readonly DiagnosticDescriptor CA1725Rule = Rule(CA1725);

        private static readonly DiagnosticDescriptor OtherRule = Rule(Other);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [CA1725Rule, OtherRule];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(ReportParameters, SymbolKind.Method);
        }

        private static DiagnosticDescriptor Rule(string id) =>
            new(id, id, id, "Test", DiagnosticSeverity.Warning, isEnabledByDefault: true);

        private static void ReportParameters(SymbolAnalysisContext context)
        {
            if (!string.Equals(context.Symbol.ContainingType.Name, "C", StringComparison.Ordinal))
            {
                return;
            }

            foreach (var parameter in ((IMethodSymbol)context.Symbol).Parameters)
            {
                context.ReportDiagnostic(Diagnostic.Create(CA1725Rule, parameter.Locations[0]));
                context.ReportDiagnostic(Diagnostic.Create(OtherRule, parameter.Locations[0]));
            }
        }
    }
}
