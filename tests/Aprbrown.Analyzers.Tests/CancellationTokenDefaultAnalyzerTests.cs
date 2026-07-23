using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;
using VerifyCS = Aprbrown.Analyzers.Tests.CSharpAnalyzerVerifier<Aprbrown.Analyzers.CancellationTokenDefaultAnalyzer>;

namespace Aprbrown.Analyzers.Tests;

/// <summary>
/// Covers every required-test row for APB0002 (spec §3.2). The two boundary tests are the ones
/// worth reading: the unresolvable-<c>CancellationToken</c> compilation, which must stay silent
/// rather than throw, and the local function, which is out of scope in v1 by decision.
/// </summary>
public sealed class CancellationTokenDefaultAnalyzerTests
{
    private const string Usings = "using System.Threading;\nusing System.Threading.Tasks;\n";

    [Fact]
    public async Task Method_with_defaulted_token_is_flagged_at_the_parameter()
    {
        const string source = Usings + "class C { Task M({|#0:CancellationToken ct = default|}) => Task.CompletedTask; }";

        await VerifyCS.VerifyAnalyzerAsync(
            source,
            VerifyCS.Diagnostic("APB0002").WithLocation(0).WithArguments("ct"));
    }

    [Fact]
    public async Task Explicit_default_expression_is_not_a_loophole()
    {
        const string source = Usings
            + "class C { Task M({|#0:CancellationToken ct = default(CancellationToken)|}) => Task.CompletedTask; }";

        await VerifyCS.VerifyAnalyzerAsync(
            source,
            VerifyCS.Diagnostic("APB0002").WithLocation(0).WithArguments("ct"));
    }

    [Fact]
    public async Task Constructor_with_defaulted_token_is_flagged()
    {
        const string source = Usings + "class C { public C({|#0:CancellationToken ct = default|}) { } }";

        await VerifyCS.VerifyAnalyzerAsync(
            source,
            VerifyCS.Diagnostic("APB0002").WithLocation(0).WithArguments("ct"));
    }

    [Fact]
    public async Task Interface_member_with_defaulted_token_is_flagged()
    {
        const string source = Usings + "interface I { Task M({|#0:CancellationToken ct = default|}); }";

        await VerifyCS.VerifyAnalyzerAsync(
            source,
            VerifyCS.Diagnostic("APB0002").WithLocation(0).WithArguments("ct"));
    }

    [Fact]
    public async Task Record_positional_token_with_a_default_is_flagged()
    {
        // Records are the one primary-constructor shape APB0001 endorses, so this is the only
        // route by which a defaulted token can reach a positional parameter list. If APB0002
        // skipped it, no rule in the set would catch it.
        const string source = Usings + "record R({|#0:CancellationToken Ct = default|});";

        await VerifyCS.VerifyAnalyzerAsync(
            source,
            VerifyCS.Diagnostic("APB0002").WithLocation(0).WithArguments("Ct"));
    }

    [Fact]
    public async Task Record_struct_positional_token_with_a_default_is_flagged()
    {
        const string source = Usings + "record struct RS({|#0:CancellationToken Ct = default|});";

        await VerifyCS.VerifyAnalyzerAsync(
            source,
            VerifyCS.Diagnostic("APB0002").WithLocation(0).WithArguments("Ct"));
    }

    [Fact]
    public async Task Record_positional_token_without_a_default_is_not_flagged()
    {
        await VerifyCS.VerifyAnalyzerAsync(Usings + "record R(CancellationToken Ct);");
    }

    [Fact]
    public async Task Explicit_construction_is_a_default_value_like_any_other()
    {
        // The rule's subject is the presence of a default clause, not the shape of the expression
        // inside it. "= new CancellationToken()" is the third form the compiler accepts here —
        // "= CancellationToken.None" is not among them, since a default must be a compile-time
        // constant and None is a property (CS1736).
        const string source = Usings
            + "class C { Task M({|#0:CancellationToken ct = new CancellationToken()|}) => Task.CompletedTask; }";

        await VerifyCS.VerifyAnalyzerAsync(
            source,
            VerifyCS.Diagnostic("APB0002").WithLocation(0).WithArguments("ct"));
    }

    [Fact]
    public async Task Token_without_a_default_is_not_flagged()
    {
        await VerifyCS.VerifyAnalyzerAsync(
            Usings + "class C { Task M(CancellationToken cancellationToken) => Task.CompletedTask; }");
    }

    [Fact]
    public async Task Defaulted_parameter_of_another_type_is_not_flagged()
    {
        await VerifyCS.VerifyAnalyzerAsync(Usings + "class C { void M(int x = 5) { } }");
    }

    [Fact]
    public async Task Each_offending_parameter_gets_its_own_diagnostic()
    {
        const string source = Usings
            + "class C { Task M({|#0:CancellationToken a = default|}, {|#1:CancellationToken b = default|}) => Task.CompletedTask; }";

        await VerifyCS.VerifyAnalyzerAsync(
            source,
            VerifyCS.Diagnostic("APB0002").WithLocation(0).WithArguments("a"),
            VerifyCS.Diagnostic("APB0002").WithLocation(1).WithArguments("b"));
    }

    [Fact]
    public async Task A_look_alike_CancellationToken_in_another_namespace_is_not_flagged()
    {
        // The rule is "type is exactly System.Threading.CancellationToken", not "named
        // CancellationToken" — a name-based implementation passes every other test and fails this.
        const string source = """
            namespace Other { struct CancellationToken { } }
            namespace App
            {
                using Other;
                class C { void M(CancellationToken ct = default) { } }
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Local_function_is_out_of_scope_in_v1()
    {
        // A recorded boundary (spec §3.2), not an oversight: changing it needs a version bump
        // under ADR-0004.
        await VerifyCS.VerifyAnalyzerAsync(
            Usings + "class C { void M() { void Local(CancellationToken ct = default) { } Local(); } }");
    }

    [Fact]
    public async Task Delegate_is_out_of_scope_in_v1()
    {
        await VerifyCS.VerifyAnalyzerAsync(Usings + "delegate void D(CancellationToken ct = default);");
    }

    [Fact]
    public async Task Compilation_without_CancellationToken_reports_nothing_and_does_not_crash()
    {
        // The verifier always supplies reference assemblies, so this case is built by hand: a
        // compilation with no references at all, where System.Threading.CancellationToken binds
        // to an error type. The source is riddled with compiler errors by construction; only the
        // analyzer's own diagnostics are asserted on.
        var compilation = CSharpCompilation.Create(
            assemblyName: "NoReferences",
            syntaxTrees: [CSharpSyntaxTree.ParseText(
                "class C { void M(System.Threading.CancellationToken ct = default) { } }")],
            references: []);

        var withAnalyzers = compilation.WithAnalyzers(
            [new CancellationTokenDefaultAnalyzer()],
            new AnalyzerOptions([]));

        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None);

        Assert.Empty(diagnostics);
    }
}
