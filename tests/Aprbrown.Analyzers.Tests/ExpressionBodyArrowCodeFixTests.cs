using Xunit;
using VerifyCS = Aprbrown.Analyzers.Tests.CSharpCodeFixVerifier<
    Aprbrown.Analyzers.ExpressionBodyArrowAnalyzer,
    Aprbrown.Analyzers.CodeFixes.ExpressionBodyArrowCodeFixProvider>;

namespace Aprbrown.Analyzers.Tests;

/// <summary>
/// Covers the APB0004 code fix: the arrow moves to the end of the previous token's line, and every
/// line between them keeps its place below it.
/// </summary>
public sealed class ExpressionBodyArrowCodeFixTests
{
    [Fact]
    public async Task Arrow_moves_to_the_end_of_the_declaration_line()
    {
        const string source = """
            class C
            {
                int M()
                    {|#0:=>|} 1;
            }
            """;

        const string fixedSource = """
            class C
            {
                int M() =>
                    1;
            }
            """;

        await VerifyCS.VerifyCodeFixAsync(
            source,
            fixedSource,
            "Move '=>' to the end of the previous line",
            VerifyCS.Diagnostic("APB0004").WithLocation(0));
    }

    [Fact]
    public async Task Trailing_comment_stays_inline_after_the_arrow()
    {
        const string source = """
            class C
            {
                int x;

                int M() // why
                    {|#0:=>|} x;
            }
            """;

        const string fixedSource = """
            class C
            {
                int x;

                int M() => // why
                    x;
            }
            """;

        await VerifyCS.VerifyCodeFixAsync(source, fixedSource, VerifyCS.Diagnostic("APB0004").WithLocation(0));
    }

    [Fact]
    public async Task Comment_line_ends_up_after_the_arrow_and_above_the_expression()
    {
        const string source = """
            class C
            {
                int M()
                    // why
                    {|#0:=>|} 1;
            }
            """;

        const string fixedSource = """
            class C
            {
                int M() =>
                    // why
                    1;
            }
            """;

        await VerifyCS.VerifyCodeFixAsync(source, fixedSource, VerifyCS.Diagnostic("APB0004").WithLocation(0));
    }

    [Fact]
    public async Task Arrow_after_a_wrapped_parameter_list_moves_up_to_the_closing_parenthesis()
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

        const string fixedSource = """
            class C
            {
                int M(
                    int first,
                    int second) =>
                    first + second;
            }
            """;

        await VerifyCS.VerifyCodeFixAsync(source, fixedSource, VerifyCS.Diagnostic("APB0004").WithLocation(0));
    }

    [Fact]
    public async Task Whitespace_trailing_the_declaration_line_is_not_left_after_the_arrow()
    {
        const string source = "class C\n{\n    int M()   \n        {|#0:=>|} 1;\n}\n";
        const string fixedSource = "class C\n{\n    int M() =>\n        1;\n}\n";

        await VerifyCS.VerifyCodeFixAsync(source, fixedSource, VerifyCS.Diagnostic("APB0004").WithLocation(0));
    }

    [Fact]
    public async Task Raw_string_expression_is_moved_below_the_arrow_unchanged()
    {
        const string source = """"
            class C
            {
                string M()
                    {|#0:=>|} """
                        text
                        """;
            }
            """";

        const string fixedSource = """"
            class C
            {
                string M() =>
                    """
                        text
                        """;
            }
            """";

        await VerifyCS.VerifyCodeFixAsync(source, fixedSource, VerifyCS.Diagnostic("APB0004").WithLocation(0));
    }

    [Fact]
    public async Task Arrow_alone_on_its_line_leaves_no_blank_line_behind()
    {
        const string source = """
            class C
            {
                int M()
                    {|#0:=>|}
                    1;
            }
            """;

        const string fixedSource = """
            class C
            {
                int M() =>
                    1;
            }
            """;

        await VerifyCS.VerifyCodeFixAsync(source, fixedSource, VerifyCS.Diagnostic("APB0004").WithLocation(0));
    }

    [Fact]
    public async Task Arrow_alone_on_its_line_below_a_trailing_comment_keeps_the_comment_inline()
    {
        const string source = """
            class C
            {
                int M() // why
                    {|#0:=>|}
                    1;
            }
            """;

        const string fixedSource = """
            class C
            {
                int M() => // why
                    1;
            }
            """;

        await VerifyCS.VerifyCodeFixAsync(source, fixedSource, VerifyCS.Diagnostic("APB0004").WithLocation(0));
    }

    [Fact]
    public async Task No_fix_is_offered_across_the_active_conditional_branch()
    {
        const string source = """
            #define DEBUG
            class C
            {
                int M()
            #if DEBUG
                    {|#0:=>|} 1;
            #else
                    => 2;
            #endif
            }
            """;

        await VerifyCS.VerifyCodeFixAsync(source, source, VerifyCS.Diagnostic("APB0004").WithLocation(0));
    }

    [Fact]
    public async Task No_fix_is_offered_across_a_conditional_directive()
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

        await VerifyCS.VerifyCodeFixAsync(source, source, VerifyCS.Diagnostic("APB0004").WithLocation(0));
    }

    [Fact]
    public async Task No_fix_is_offered_across_a_region_directive()
    {
        const string source = """
            class C
            {
                int M()
                #region body
                    {|#0:=>|} 1;
                #endregion
            }
            """;

        await VerifyCS.VerifyCodeFixAsync(source, source, VerifyCS.Diagnostic("APB0004").WithLocation(0));
    }

    [Fact]
    public async Task Fix_all_fixes_every_arrow_in_the_document_in_one_pass()
    {
        const string source = """
            class C
            {
                int x;

                int M()
                    {|#0:=>|} 1;

                int P
                    {|#1:=>|} 2;

                C()
                    {|#2:=>|} x = 3;
            }
            """;

        const string fixedSource = """
            class C
            {
                int x;

                int M() =>
                    1;

                int P =>
                    2;

                C() =>
                    x = 3;
            }
            """;

        await VerifyCS.VerifyFixAllInOnePassAsync(
            source,
            fixedSource,
            VerifyCS.Diagnostic("APB0004").WithLocation(0),
            VerifyCS.Diagnostic("APB0004").WithLocation(1),
            VerifyCS.Diagnostic("APB0004").WithLocation(2));
    }
}
