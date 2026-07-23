using Aprbrown.Analyzers.CodeFixes;
using Xunit;
using VerifyCS = Aprbrown.Analyzers.Tests.CSharpCodeFixVerifier<
    Aprbrown.Analyzers.InterfaceParameterNameAnalyzer,
    Aprbrown.Analyzers.CodeFixes.InterfaceParameterNameCodeFixProvider>;

namespace Aprbrown.Analyzers.Tests;

/// <summary>
/// Covers the APB0003 code fix (spec §3.4). Every case here renames the *symbol*: each fixed
/// source is compiled by the harness, so a fix that rewrote only the declaration token would fail
/// on the dangling body reference rather than pass quietly.
/// </summary>
public sealed class InterfaceParameterNameCodeFixTests
{
    [Fact]
    public async Task Body_reference_is_renamed_with_the_declaration()
    {
        const string source = """
            using System;
            interface I { void M(int alpha); }
            class C : I { public void M(int {|#0:beta|}) { Console.WriteLine(beta); } }
            """;

        const string fixedSource = """
            using System;
            interface I { void M(int alpha); }
            class C : I { public void M(int alpha) { Console.WriteLine(alpha); } }
            """;

        await VerifyCS.VerifyCodeFixAsync(
            source,
            fixedSource,
            VerifyCS.Diagnostic("APB0003").WithLocation(0).WithArguments("beta", "alpha", "I.M"));
    }

    [Fact]
    public async Task Fix_is_offered_with_the_expected_name_in_its_title()
    {
        const string source = """
            interface I { void M(int alpha); }
            class C : I { public void M(int {|#0:beta|}) { } }
            """;

        const string fixedSource = """
            interface I { void M(int alpha); }
            class C : I { public void M(int alpha) { } }
            """;

        await VerifyCS.VerifyCodeFixAsync(
            source,
            fixedSource,
            "Rename parameter to 'alpha'",
            VerifyCS.Diagnostic("APB0003").WithLocation(0).WithArguments("beta", "alpha", "I.M"));
    }

    [Fact]
    public void No_batch_fix_all_provider_is_registered()
    {
        // Spec §3.4: two renames applied to one document from a stale solution snapshot can
        // conflict or clobber each other, so the fix is single-invocation only in v1. Null here is
        // what keeps "Fix all occurrences" out of the IDE's light-bulb menu.
        Assert.Null(new InterfaceParameterNameCodeFixProvider().GetFixAllProvider());
    }

    [Fact]
    public async Task Explicit_implementation_is_fixed()
    {
        const string source = """
            using System;
            interface I { void M(int alpha); }
            class C : I { void I.M(int {|#0:beta|}) { Console.WriteLine(beta); } }
            """;

        const string fixedSource = """
            using System;
            interface I { void M(int alpha); }
            class C : I { void I.M(int alpha) { Console.WriteLine(alpha); } }
            """;

        await VerifyCS.VerifyCodeFixAsync(
            source,
            fixedSource,
            VerifyCS.Diagnostic("APB0003").WithLocation(0).WithArguments("beta", "alpha", "I.M"));
    }

    [Fact]
    public async Task Generic_interface_implementation_is_fixed()
    {
        const string source = """
            using System;
            interface I<T> { void M(T alpha); }
            class C : I<int> { public void M(int {|#0:beta|}) { Console.WriteLine(beta); } }
            """;

        const string fixedSource = """
            using System;
            interface I<T> { void M(T alpha); }
            class C : I<int> { public void M(int alpha) { Console.WriteLine(alpha); } }
            """;

        await VerifyCS.VerifyCodeFixAsync(
            source,
            fixedSource,
            VerifyCS.Diagnostic("APB0003").WithLocation(0).WithArguments("beta", "alpha", "I.M"));
    }

    [Fact]
    public async Task Each_mismatched_parameter_is_fixed_in_turn()
    {
        // Two diagnostics on one method, fixed one invocation at a time — which is all the IDE
        // offers without a FixAllProvider, and is exactly how the harness applies them.
        const string source = """
            using System;
            interface I { void M(int alpha, int gamma); }
            class C : I
            {
                public void M(int {|#0:beta|}, int {|#1:delta|}) { Console.WriteLine(beta + delta); }
            }
            """;

        const string fixedSource = """
            using System;
            interface I { void M(int alpha, int gamma); }
            class C : I
            {
                public void M(int alpha, int gamma) { Console.WriteLine(alpha + gamma); }
            }
            """;

        await VerifyCS.VerifyCodeFixAsync(
            source,
            fixedSource,
            VerifyCS.Diagnostic("APB0003").WithLocation(0).WithArguments("beta", "alpha", "I.M"),
            VerifyCS.Diagnostic("APB0003").WithLocation(1).WithArguments("delta", "gamma", "I.M"));
    }

    [Fact]
    public async Task No_fix_is_offered_when_a_local_already_holds_the_expected_name()
    {
        // Renamer does not resolve this collision — it records a rename conflict and returns
        // source that does not compile. The fix withdraws instead; the diagnostic stands and the
        // developer settles the naming by hand. Fixed source identical to the input is the
        // harness's way of asserting nothing was applied.
        const string source = """
            using System;
            interface I { void M(int alpha); }
            class C : I
            {
                public void M(int {|#0:beta|})
                {
                    var alpha = beta + 1;
                    Console.WriteLine(alpha);
                }
            }
            """;

        await VerifyCS.VerifyCodeFixAsync(
            source,
            source,
            VerifyCS.Diagnostic("APB0003").WithLocation(0).WithArguments("beta", "alpha", "I.M"));
    }

    [Fact]
    public async Task A_static_local_function_may_reuse_the_expected_name()
    {
        // A static local function captures nothing, so C# lets it declare a name the enclosing
        // method already uses. The collision guard must not read that as a conflict — the rename
        // is legal and the fix is owed to the developer.
        const string source = """
            using System;
            interface I { void M(int alpha); }
            class C : I
            {
                public void M(int {|#0:beta|})
                {
                    static int Double(int alpha) => alpha * 2;
                    Console.WriteLine(Double(beta));
                }
            }
            """;

        const string fixedSource = """
            using System;
            interface I { void M(int alpha); }
            class C : I
            {
                public void M(int alpha)
                {
                    static int Double(int alpha) => alpha * 2;
                    Console.WriteLine(Double(alpha));
                }
            }
            """;

        await VerifyCS.VerifyCodeFixAsync(
            source,
            fixedSource,
            VerifyCS.Diagnostic("APB0003").WithLocation(0).WithArguments("beta", "alpha", "I.M"));
    }

    [Fact]
    public async Task No_fix_is_offered_when_a_non_static_local_function_holds_the_expected_name()
    {
        // The non-static counterpart of the case above: it captures, so shadowing is CS0136 and
        // the rename really would not compile.
        const string source = """
            using System;
            interface I { void M(int alpha); }
            class C : I
            {
                public void M(int {|#0:beta|})
                {
                    int Double(int alpha) => alpha * 2;
                    Console.WriteLine(Double(beta));
                }
            }
            """;

        await VerifyCS.VerifyCodeFixAsync(
            source,
            source,
            VerifyCS.Diagnostic("APB0003").WithLocation(0).WithArguments("beta", "alpha", "I.M"));
    }

    [Fact]
    public async Task No_fix_is_offered_when_another_parameter_already_holds_the_expected_name()
    {
        // Swapped names: renaming 'beta' to 'alpha' would collide with the second parameter, and
        // renaming that one to 'beta' collides right back. Neither is offered.
        const string source = """
            interface I { void M(int alpha, int beta); }
            class C : I { public void M(int {|#0:beta|}, int {|#1:alpha|}) { } }
            """;

        await VerifyCS.VerifyCodeFixAsync(
            source,
            source,
            VerifyCS.Diagnostic("APB0003").WithLocation(0).WithArguments("beta", "alpha", "I.M"),
            VerifyCS.Diagnostic("APB0003").WithLocation(1).WithArguments("alpha", "beta", "I.M"));
    }
}
