using Xunit;
using VerifyCS = Aprbrown.Analyzers.Tests.CSharpAnalyzerVerifier<Aprbrown.Analyzers.TrailingCommentAnalyzer>;

namespace Aprbrown.Analyzers.Tests;

/// <summary>
/// Covers what APB0005 reports: a <c>//</c> comment sharing its line with code before it, and
/// nothing else.
/// </summary>
public sealed class TrailingCommentAnalyzerTests
{
    [Fact]
    public async Task Comment_after_a_statement_is_flagged_on_the_comment()
    {
        const string source = """
            class C
            {
                void M()
                {
                    var x = 1; {|#0:// one|}
                }
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source, VerifyCS.Diagnostic("APB0005").WithLocation(0));
    }

    [Fact]
    public async Task Comment_after_a_field_is_flagged()
    {
        const string source = """
            class C
            {
                private int _count; {|#0:// how many|}
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source, VerifyCS.Diagnostic("APB0005").WithLocation(0));
    }

    [Fact]
    public async Task Comment_after_a_list_item_is_flagged()
    {
        const string source = """
            class C
            {
                int[] _values =
                [
                    1, {|#0:// first|}
                    2,
                ];
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source, VerifyCS.Diagnostic("APB0005").WithLocation(0));
    }

    [Fact]
    public async Task Comment_after_a_closing_parenthesis_is_flagged()
    {
        const string source = """
            class C
            {
                int M(int x) => M(
                    x) {|#0:// recurse|}
                    + 1;
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source, VerifyCS.Diagnostic("APB0005").WithLocation(0));
    }

    [Fact]
    public async Task Comment_after_an_opening_brace_is_flagged()
    {
        const string source = """
            class C
            { {|#0:// members|}
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source, VerifyCS.Diagnostic("APB0005").WithLocation(0));
    }

    [Fact]
    public async Task Comment_after_an_expression_body_arrow_is_flagged()
    {
        const string source = """
            class C
            {
                int M() => {|#0:// why|}
                    1;
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source, VerifyCS.Diagnostic("APB0005").WithLocation(0));
    }

    [Fact]
    public async Task Comment_on_its_own_line_is_not_flagged()
    {
        const string source = """
            class C
            {
                // how many
                private int _count;
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Trailing_block_comment_is_not_flagged()
    {
        const string source = """
            class C
            {
                private int _count; /* how many */
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Inline_block_comment_labelling_an_argument_is_not_flagged()
    {
        const string source = """
            class C
            {
                int M(int retries) => M(/* retries */ 3);
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Documentation_is_not_flagged()
    {
        const string source = """
            /// <summary>A type.</summary>
            class C
            {
                /// <summary>How many.</summary>
                private int _count;
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Comment_on_an_endif_is_not_flagged()
    {
        const string source = """
            class C
            {
            #if DEBUG
                private int _count;
            #endif // DEBUG
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Justification_on_a_pragma_is_not_flagged()
    {
        const string source = """
            class C
            {
            #pragma warning disable CA1234 // reason
                private int _count;
            #pragma warning restore CA1234
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Theory]
    [InlineData("// NOSONAR")]
    [InlineData("//NOSONAR because")]
    [InlineData("// ReSharper disable once UnusedMember.Local")]
    [InlineData("// lang=json")]
    [InlineData("// language=regex")]
    public async Task Tool_directive_is_not_flagged(string comment)
    {
        var source = $$"""
            class C
            {
                private string _pattern = "a"; {{comment}}
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Theory]
    [InlineData("// ReSharperish")]
    [InlineData("// language is English")]
    [InlineData("// expect: APB0001 warning")]
    public async Task Comment_that_only_resembles_a_tool_directive_is_flagged(string comment)
    {
        var source = $$"""
            class C
            {
                private string _pattern = "a"; {|#0:{{comment}}|}
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source, VerifyCS.Diagnostic("APB0005").WithLocation(0));
    }

    [Fact]
    public async Task Comment_after_a_multi_line_raw_string_is_flagged()
    {
        const string source = """"
            class C
            {
                private string _json = """
                    {}
                    """; {|#0:// empty|}
            }
            """";

        await VerifyCS.VerifyAnalyzerAsync(source, VerifyCS.Diagnostic("APB0005").WithLocation(0));
    }
}
