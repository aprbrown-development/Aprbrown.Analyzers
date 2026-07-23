using Xunit;
using VerifyCS = Aprbrown.Analyzers.Tests.CSharpAnalyzerVerifier<Aprbrown.Analyzers.InterfaceParameterNameAnalyzer>;

namespace Aprbrown.Analyzers.Tests;

/// <summary>
/// Covers every required-test row for APB0003 (spec §3.3).
/// </summary>
/// <remarks>
/// Three of the spec's four must-not-flag cases are pinned here: an interface declaration is not
/// an implementer, accessor parameters are synthesised, and an inherited implementation is
/// reported on the base that declares it rather than on the derived type. The fourth — a
/// parameter with no source location — cannot be written as a test, because a metadata
/// implementation is excluded before the guard is reached; see the analyzer's comment there.
/// </remarks>
public sealed class InterfaceParameterNameAnalyzerTests
{
    [Fact]
    public async Task Mismatched_parameter_is_flagged_with_the_interface_name()
    {
        const string source = """
            interface I { void M(int alpha); }
            class C : I { public void M(int {|#0:beta|}) { } }
            """;

        await VerifyCS.VerifyAnalyzerAsync(
            source,
            VerifyCS.Diagnostic("APB0003").WithLocation(0).WithArguments("beta", "alpha", "I.M"));
    }

    [Fact]
    public async Task Matching_parameter_is_not_flagged()
    {
        const string source = """
            interface I { void M(int alpha); }
            class C : I { public void M(int alpha) { } }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Explicit_implementation_is_flagged()
    {
        const string source = """
            interface I { void M(int alpha); }
            class C : I { void I.M(int {|#0:beta|}) { } }
            """;

        await VerifyCS.VerifyAnalyzerAsync(
            source,
            VerifyCS.Diagnostic("APB0003").WithLocation(0).WithArguments("beta", "alpha", "I.M"));
    }

    [Fact]
    public async Task Struct_implementation_is_flagged()
    {
        const string source = """
            interface I { void M(int alpha); }
            struct S : I { public void M(int {|#0:beta|}) { } }
            """;

        await VerifyCS.VerifyAnalyzerAsync(
            source,
            VerifyCS.Diagnostic("APB0003").WithLocation(0).WithArguments("beta", "alpha", "I.M"));
    }

    [Fact]
    public async Task Implemented_property_is_not_flagged()
    {
        // The setter's 'value' parameter is synthesised by the compiler; there is no name the
        // author could rename.
        const string source = """
            interface I { int P { get; set; } }
            class C : I { public int P { get; set; } }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Implemented_event_is_not_flagged()
    {
        const string source = """
            interface I { event System.Action E; }
            class C : I { public event System.Action E { add { } remove { } } }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Interface_declaration_is_not_flagged()
    {
        // Only implementers are policed. A default interface implementation still lives on an
        // interface, so it stays silent.
        const string source = """
            interface I { void M(int alpha); }
            interface J : I { void I.M(int beta) { } }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Implementation_inherited_from_a_base_is_flagged_on_the_base_only()
    {
        // Reporting on Derived would point at code the developer cannot edit there.
        const string source = """
            interface I { void M(int alpha); }
            class Base : I { public void M(int {|#0:beta|}) { } }
            class Derived : Base, I { }
            """;

        await VerifyCS.VerifyAnalyzerAsync(
            source,
            VerifyCS.Diagnostic("APB0003").WithLocation(0).WithArguments("beta", "alpha", "I.M"));
    }

    [Fact]
    public async Task Generic_interface_implementation_is_flagged()
    {
        const string source = """
            interface I<T> { void M(T alpha); }
            class C : I<int> { public void M(int {|#0:beta|}) { } }
            """;

        await VerifyCS.VerifyAnalyzerAsync(
            source,
            VerifyCS.Diagnostic("APB0003").WithLocation(0).WithArguments("beta", "alpha", "I.M"));
    }

    [Fact]
    public async Task Only_the_mismatched_interface_of_two_is_flagged()
    {
        const string source = """
            interface I { void M(int alpha); }
            interface J { void N(int gamma); }
            class C : I, J
            {
                public void M(int {|#0:beta|}) { }

                public void N(int gamma) { }
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(
            source,
            VerifyCS.Diagnostic("APB0003").WithLocation(0).WithArguments("beta", "alpha", "I.M"));
    }

    [Fact]
    public async Task Each_mismatched_parameter_gets_its_own_diagnostic()
    {
        const string source = """
            interface I { void M(int alpha, int gamma); }
            class C : I { public void M(int {|#0:beta|}, int {|#1:delta|}) { } }
            """;

        await VerifyCS.VerifyAnalyzerAsync(
            source,
            VerifyCS.Diagnostic("APB0003").WithLocation(0).WithArguments("beta", "alpha", "I.M"),
            VerifyCS.Diagnostic("APB0003").WithLocation(1).WithArguments("delta", "gamma", "I.M"));
    }
}
