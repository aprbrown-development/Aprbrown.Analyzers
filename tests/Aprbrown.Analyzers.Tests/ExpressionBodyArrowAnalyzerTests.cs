using Xunit;
using VerifyCS = Aprbrown.Analyzers.Tests.CSharpAnalyzerVerifier<Aprbrown.Analyzers.ExpressionBodyArrowAnalyzer>;

namespace Aprbrown.Analyzers.Tests;

/// <summary>
/// Covers APB0004's analyzer: every expression-bodied member whose arrow starts a line is flagged
/// at the arrow, while an arrow that ends the declaration line, and the arrows of lambdas and
/// switch-expression arms, are left alone.
/// </summary>
public sealed class ExpressionBodyArrowAnalyzerTests
{
    [Fact]
    public async Task Arrow_first_method_is_flagged_at_the_arrow()
    {
        const string source = """
            class C
            {
                int M()
                    {|#0:=>|} 1;
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source, VerifyCS.Diagnostic("APB0004").WithLocation(0));
    }

    [Theory]
    [InlineData("C()\n        {|#0:=>|} System.Console.WriteLine();")]
    [InlineData("~C()\n        {|#0:=>|} System.Console.WriteLine();")]
    [InlineData("public static C operator +(C left, C right)\n        {|#0:=>|} left;")]
    [InlineData("public static implicit operator int(C value)\n        {|#0:=>|} 0;")]
    [InlineData("int P\n        {|#0:=>|} 1;")]
    [InlineData("int this[int index]\n        {|#0:=>|} index;")]
    [InlineData("int P\n    {\n        get\n            {|#0:=>|} 1;\n    }")]
    [InlineData("void M()\n    {\n        int Local()\n            {|#0:=>|} 1;\n        Local();\n    }")]
    public async Task Arrow_first_member_of_every_kind_is_flagged(string member)
    {
        var source = "class C\n{\n    " + member + "\n}\n";

        await VerifyCS.VerifyAnalyzerAsync(source, VerifyCS.Diagnostic("APB0004").WithLocation(0));
    }

    [Fact]
    public async Task Arrow_after_a_wrapped_parameter_list_is_flagged()
    {
        const string source = """
            class C
            {
                int M(
                    int first,
                    int second)
                    {|#0:=>|} first + second;
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source, VerifyCS.Diagnostic("APB0004").WithLocation(0));
    }

    [Fact]
    public async Task Arrow_below_a_comment_line_is_flagged()
    {
        const string source = """
            class C
            {
                int M()
                    // why
                    {|#0:=>|} 1;
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source, VerifyCS.Diagnostic("APB0004").WithLocation(0));
    }

    [Fact]
    public async Task Arrow_at_the_end_of_the_declaration_line_is_not_flagged()
    {
        const string source = """
            class C
            {
                int M() =>
                    1;
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Single_line_member_is_not_flagged() =>
        await VerifyCS.VerifyAnalyzerAsync("class C { int M() => 1; }");

    [Fact]
    public async Task Arrow_first_lambda_is_not_flagged()
    {
        const string source = """
            using System;
            class C
            {
                Func<int, int> f = x
                    => x + 1;
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Arrow_first_switch_expression_arm_is_not_flagged()
    {
        const string source = """
            class C
            {
                string M(int x)
                {
                    return x switch
                    {
                        0
                            => "zero",
                        _
                            => "other",
                    };
                }
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Arrow_first_inside_a_conditional_directive_is_flagged()
    {
        const string source = """
            class C
            {
                int M()
            #if DEBUG
                    => 1;
            #else
                    {|#0:=>|} 2;
            #endif
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source, VerifyCS.Diagnostic("APB0004").WithLocation(0));
    }
}
