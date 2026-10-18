using Xunit;
using VerifyCS = Aprbrown.Analyzers.Tests.CSharpAnalyzerVerifier<Aprbrown.Analyzers.NestedConditionalAnalyzer>;

namespace Aprbrown.Analyzers.Tests;

/// <summary>
/// Covers what APB0006 reports: a conditional expression inside another conditional expression's
/// condition or branches, and nothing else.
/// </summary>
public sealed class NestedConditionalAnalyzerTests
{
    [Fact]
    public async Task Conditional_in_the_false_branch_is_flagged_on_the_inner_conditional()
    {
        const string source = """
            class C
            {
                int M(bool a, bool b) => a ? 1 : {|#0:b ? 2 : 3|};
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source, VerifyCS.Diagnostic("APB0006").WithLocation(0));
    }

    [Fact]
    public async Task Conditional_in_the_true_branch_is_flagged()
    {
        const string source = """
            class C
            {
                int M(bool a, bool b) => a ? {|#0:b ? 1 : 2|} : 3;
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source, VerifyCS.Diagnostic("APB0006").WithLocation(0));
    }

    [Fact]
    public async Task Conditional_in_the_condition_is_flagged()
    {
        const string source = """
            class C
            {
                int M(bool a, bool b, bool c) => ({|#0:a ? b : c|}) ? 1 : 2;
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source, VerifyCS.Diagnostic("APB0006").WithLocation(0));
    }

    [Theory]
    [InlineData("(int)(b ? 2L : 3L)", "b ? 2L : 3L")]
    [InlineData("Twice(b ? 2 : 3)", "b ? 2 : 3")]
    [InlineData("1 + (b ? 2 : 3) * 4", "b ? 2 : 3")]
    [InlineData("new[] { b ? 2 : 3 }[0]", "b ? 2 : 3")]
    public async Task Conditional_wrapped_in_other_expressions_is_flagged(string branch, string inner)
    {
        var marked = branch.Replace(inner, "{|#0:" + inner + "|}", StringComparison.Ordinal);
        var source = $$"""
            class C
            {
                int M(bool a, bool b) => a ? 1 : {{marked}};

                static int Twice(int x) => x * 2;
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source, VerifyCS.Diagnostic("APB0006").WithLocation(0));
    }

    [Fact]
    public async Task Three_deep_chain_is_flagged_once_per_nested_conditional()
    {
        const string source = """
            class C
            {
                int M(bool a, bool b, bool c) => a ? 1 : {|#0:b ? 2 : {|#1:c ? 3 : 4|}|};
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(
            source,
            VerifyCS.Diagnostic("APB0006").WithLocation(0),
            VerifyCS.Diagnostic("APB0006").WithLocation(1));
    }

    [Fact]
    public async Task Single_conditional_is_not_flagged()
    {
        const string source = """
            class C
            {
                int M(bool a) => a ? 1 : 2;
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Sibling_conditionals_in_one_statement_are_not_flagged()
    {
        const string source = """
            class C
            {
                int M(bool a, bool b) => (a ? 1 : 2) + (b ? 3 : 4);
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Nesting_inside_a_lambda_is_judged_within_the_lambda()
    {
        const string source = """
            using System;

            class C
            {
                Func<int, int> M(bool a) => a ? x => x > 0 ? {|#0:x > 9 ? 2 : 1|} : 0 : x => x;
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source, VerifyCS.Diagnostic("APB0006").WithLocation(0));
    }

    [Theory]
    [InlineData("a ? s ?? \"none\" : \"\"")]
    [InlineData("a ? s?.Trim() : null")]
    [InlineData("a ? (s ??= \"none\") : \"\"")]
    [InlineData("a ? s switch { null => \"none\", _ => s } : \"\"")]
    [InlineData("s ?? (a ? \"yes\" : \"no\")")]
    [InlineData("s?.Insert(0, a ? \"yes\" : \"no\")")]
    [InlineData("s switch { null => a ? \"yes\" : \"no\", _ => s }")]
    public async Task Operators_that_are_not_conditional_expressions_neither_report_nor_nest(string expression)
    {
        var source = $$"""
            #nullable enable

            class C
            {
                string? M(bool a, string? s) => {{expression}};
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Conditional_in_a_switch_arm_inside_a_conditional_is_flagged()
    {
        const string source = """
            class C
            {
                int M(bool a, bool b, int n) => a ? n switch { 0 => {|#0:b ? 1 : 2|}, _ => 3 } : 4;
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source, VerifyCS.Diagnostic("APB0006").WithLocation(0));
    }

    [Fact]
    public async Task Conditional_in_a_statement_lambda_inside_an_if_is_not_flagged()
    {
        const string source = """
            using System;

            class C
            {
                void M(bool a)
                {
                    if (a)
                    {
                        Func<int, int> f = x => x > 0 ? 1 : 2;
                    }
                }
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Theory]
    [InlineData("x => x > 0 ? 1 : 2")]
    [InlineData("(int x) => { return x > 0 ? 1 : 2; }")]
    [InlineData("delegate (int x) { return x > 0 ? 1 : 2; }")]
    [InlineData("x => { int Sign(int y) => y > 0 ? 1 : 2; return Sign(x); }")]
    public async Task Conditional_in_a_new_scope_inside_a_conditional_is_not_flagged(string function)
    {
        var source = $$"""
            using System;

            class C
            {
                Func<int, int> M(bool a) => a ? {{function}} : (Func<int, int>)(x => x);
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }
}
