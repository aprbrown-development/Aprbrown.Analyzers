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
        => await VerifyCS.VerifyAnalyzerAsync(Usings + "record R(CancellationToken Ct);");

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
        => await VerifyCS.VerifyAnalyzerAsync(
            Usings + "class C { Task M(CancellationToken cancellationToken) => Task.CompletedTask; }");

    [Fact]
    public async Task Defaulted_parameter_of_another_type_is_not_flagged()
        => await VerifyCS.VerifyAnalyzerAsync(Usings + "class C { void M(int x = 5) { } }");

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

    // A recorded boundary (spec §3.2), not an oversight: changing it needs a version bump
    // under ADR-0004.
    [Fact]
    public async Task Local_function_is_out_of_scope_in_v1()
        => await VerifyCS.VerifyAnalyzerAsync(
            Usings + "class C { void M() { void Local(CancellationToken ct = default) { } Local(); } }");

    [Fact]
    public async Task Delegate_is_out_of_scope_in_v1()
        => await VerifyCS.VerifyAnalyzerAsync(
            Usings + "delegate void D(CancellationToken ct = default);");

    [Fact]
    public async Task Override_keeping_a_default_its_base_declares_is_not_flagged()
    {
        // The case MA0061 forces: a call site binds to the declared type, so the base's default
        // applies whatever the override says, and MA0061 demands the override keep it. The base
        // is a framework member here, so the default is not the author's to remove.
        const string source = Usings + """
            using System;
            using System.IO;
            abstract class S : Stream
            {
                public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
                    => new(0);
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Override_of_a_base_in_the_same_source_keeping_its_default_is_not_flagged()
    {
        // The base answers for the default once, where the author chose it.
        const string source = Usings + """
            abstract class B { public abstract Task RunAsync({|#0:CancellationToken ct = default|}); }
            class D : B { public override Task RunAsync(CancellationToken ct = default) => Task.CompletedTask; }
            """;

        await VerifyCS.VerifyAnalyzerAsync(
            source,
            VerifyCS.Diagnostic("APB0002").WithLocation(0).WithArguments("ct"));
    }

    [Fact]
    public async Task Override_introducing_a_default_its_base_lacks_is_flagged()
    {
        // The exemption follows the base's default; it is not a blanket pass for overrides. Here
        // dropping the default satisfies both APB0002 and MA0061, so the rule must keep reporting.
        const string source = Usings + """
            abstract class B { public abstract Task RunAsync(CancellationToken ct); }
            class D : B { public override Task RunAsync({|#0:CancellationToken ct = default|}) => Task.CompletedTask; }
            """;

        await VerifyCS.VerifyAnalyzerAsync(
            source,
            VerifyCS.Diagnostic("APB0002").WithLocation(0).WithArguments("ct"));
    }

    [Fact]
    public async Task Implicit_implementation_keeping_the_interface_default_is_not_flagged()
    {
        // The interface declaration carries its own APB0002, so the default is reported once,
        // where the author chose it, and not again on every implementation MA0061 makes keep it.
        const string source = Usings + """
            interface I { Task M({|#0:CancellationToken ct = default|}); }
            class C : I { public Task M(CancellationToken ct = default) => Task.CompletedTask; }
            """;

        await VerifyCS.VerifyAnalyzerAsync(
            source,
            VerifyCS.Diagnostic("APB0002").WithLocation(0).WithArguments("ct"));
    }

    [Fact]
    public async Task Explicit_implementation_keeping_the_interface_default_is_not_flagged()
    {
        // The compiler already warns (CS1066) that a default here has no effect. That is the
        // compiler's concern, not this rule's: the exemption holds on the same terms as above.
        const string source = Usings + """
            interface I { Task M({|#0:CancellationToken ct = default|}); }
            class C : I { Task I.M(CancellationToken ct = default) => Task.CompletedTask; }
            """;

        await VerifyCS.VerifyAnalyzerAsync(
            source,
            VerifyCS.Diagnostic("APB0002").WithLocation(0).WithArguments("ct"));
    }

    [Fact]
    public async Task Implementation_of_a_generic_interface_keeping_its_default_is_not_flagged()
    {
        // The implementation is matched against the constructed interface member, not its
        // definition, so a generic interface must not slip past the identity comparison.
        const string source = Usings + """
            interface I<T> { Task M(T item, {|#0:CancellationToken ct = default|}); }
            class C<T> : I<T> { public Task M(T item, CancellationToken ct = default) => Task.CompletedTask; }
            class D : I<int> { public Task M(int item, CancellationToken ct = default) => Task.CompletedTask; }
            """;

        await VerifyCS.VerifyAnalyzerAsync(
            source,
            VerifyCS.Diagnostic("APB0002").WithLocation(0).WithArguments("ct"));
    }

    [Fact]
    public async Task Default_implementation_on_a_derived_interface_keeping_the_default_is_not_flagged()
    {
        // Unlike a "new" redeclaration, an explicit implementation in an interface body implements
        // the member, so it is exempt on the same terms as one in a class.
        const string source = Usings + """
            interface I { Task M({|#0:CancellationToken ct = default|}); }
            interface J : I { Task I.M(CancellationToken ct = default) => Task.CompletedTask; }
            """;

        await VerifyCS.VerifyAnalyzerAsync(
            source,
            VerifyCS.Diagnostic("APB0002").WithLocation(0).WithArguments("ct"));
    }

    [Fact]
    public async Task Override_of_an_override_answers_to_the_member_it_directly_overrides()
    {
        // The middle override drops the default, so it is the last word a call through B sees.
        // The leaf re-adding it is introducing a default, not keeping one, even though the root of
        // the chain declares it. The root, declared in the same source, carries its own APB0002.
        const string source = Usings + """
            abstract class A { public abstract Task RunAsync({|#0:CancellationToken ct = default|}); }
            abstract class B : A { public override Task RunAsync(CancellationToken ct) => Task.CompletedTask; }
            class C : B { public override Task RunAsync({|#1:CancellationToken ct = default|}) => Task.CompletedTask; }
            """;

        await VerifyCS.VerifyAnalyzerAsync(
            source,
            VerifyCS.Diagnostic("APB0002").WithLocation(0).WithArguments("ct"),
            VerifyCS.Diagnostic("APB0002").WithLocation(1).WithArguments("ct"));
    }

    [Fact]
    public async Task Interface_member_redeclared_on_a_derived_interface_is_flagged()
    {
        // A redeclaration is a declaration, not an implementation, so it answers for its own default.
        const string source = Usings + """
            interface I { Task M({|#0:CancellationToken ct = default|}); }
            interface J : I { new Task M({|#1:CancellationToken ct = default|}); }
            """;

        await VerifyCS.VerifyAnalyzerAsync(
            source,
            VerifyCS.Diagnostic("APB0002").WithLocation(0).WithArguments("ct"),
            VerifyCS.Diagnostic("APB0002").WithLocation(1).WithArguments("ct"));
    }

    [Fact]
    public async Task Implementing_several_members_at_once_is_exempt_if_any_of_them_defaults()
    {
        // MA0061 fires when any implemented member's parameter is defaulted, so that is when
        // APB0002 must stand aside.
        const string source = Usings + """
            interface IPlain { Task M(CancellationToken ct); }
            interface IDefaulted { Task M({|#0:CancellationToken ct = default|}); }
            class C : IPlain, IDefaulted { public Task M(CancellationToken ct = default) => Task.CompletedTask; }
            """;

        await VerifyCS.VerifyAnalyzerAsync(
            source,
            VerifyCS.Diagnostic("APB0002").WithLocation(0).WithArguments("ct"));
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
                "class C { void M(System.Threading.CancellationToken ct = default) { } }",
                cancellationToken: CancellationToken.None)],
            references: []);

        var withAnalyzers = compilation.WithAnalyzers(
            [new CancellationTokenDefaultAnalyzer()],
            new AnalyzerOptions([]));

        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Half_typed_overrides_and_implementations_do_not_crash_the_analyzer()
    {
        // The IDE runs the analyzer against code mid-edit, where an override or implementation
        // and its member need not agree on arity. Any throw would surface as AD0001 and take down
        // the whole analysis, so it is asserted absent; the compiler errors are expected and ignored.
        // These shapes do not reach the analyzer's bounds check: measured, the compiler binds none
        // of them to their member, so they pin "no crash" rather than the check itself, which
        // stays as a guard against whatever error recovery the compiler does hand back.
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            abstract class B { public abstract Task M(CancellationToken ct = default); }
            class D : B { public override Task M(int extra, CancellationToken ct = default) => Task.CompletedTask; }
            interface I { Task M(CancellationToken ct = default); }
            class C : I { Task I.M(int extra, CancellationToken ct = default) => Task.CompletedTask; public Task M(int a, int b, CancellationToken ct = default) => Task.CompletedTask; }
            """;

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            assemblyName: "HalfTyped",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, cancellationToken: CancellationToken.None)],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var withAnalyzers = compilation.WithAnalyzers(
            [new CancellationTokenDefaultAnalyzer()],
            new AnalyzerOptions([]));

        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None);

        // An analyzer exception arrives here as AD0001, so "only APB0002" rules it out.
        Assert.All(diagnostics, diagnostic => Assert.Equal("APB0002", diagnostic.Id));
    }
}
