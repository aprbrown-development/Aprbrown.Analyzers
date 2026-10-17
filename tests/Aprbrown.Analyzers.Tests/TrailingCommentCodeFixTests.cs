using Xunit;
using VerifyCS = Aprbrown.Analyzers.Tests.CSharpCodeFixVerifier<
    Aprbrown.Analyzers.TrailingCommentAnalyzer,
    Aprbrown.Analyzers.CodeFixes.TrailingCommentCodeFixProvider>;

namespace Aprbrown.Analyzers.Tests;

/// <summary>
/// Covers the APB0005 code fix, which hoists a trailing comment onto its own line directly above
/// the line it trailed, at that line's indentation.
/// </summary>
public sealed class TrailingCommentCodeFixTests
{
    [Fact]
    public async Task Comment_is_hoisted_above_its_statement_at_the_same_indentation()
    {
        const string source = """
            class C
            {
                void M()
                {
                    int x;
                    x = 2; {|#0:// Green|}
                }
            }
            """;

        const string fixedSource = """
            class C
            {
                void M()
                {
                    int x;
                    // Green
                    x = 2;
                }
            }
            """;

        await VerifyCS.VerifyCodeFixAsync(
            source,
            fixedSource,
            "Move this comment onto its own line above the code",
            VerifyCS.Diagnostic("APB0005").WithLocation(0));
    }

    [Fact]
    public async Task Comment_at_column_zero_is_hoisted_with_no_indentation()
    {
        const string source = """
            class C { } {|#0:// Green|}
            """;

        const string fixedSource = """
            // Green
            class C { }
            """;

        await VerifyCS.VerifyCodeFixAsync(source, fixedSource, VerifyCS.Diagnostic("APB0005").WithLocation(0));
    }

    [Fact]
    public async Task Each_comment_in_a_query_chain_is_hoisted_above_its_own_call_in_one_pass()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                List<string> M(IEnumerable<string> users)
                {
                    var admins = users
                        .Select(user => user.Trim())
                        .Where(user => user.StartsWith("admin")) {|#0:// Admin users|}
                        .Where(user => user.Length > 5) {|#1:// Active users|}
                        .ToList();
                    return admins;
                }
            }
            """;

        const string fixedSource = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                List<string> M(IEnumerable<string> users)
                {
                    var admins = users
                        .Select(user => user.Trim())
                        // Admin users
                        .Where(user => user.StartsWith("admin"))
                        // Active users
                        .Where(user => user.Length > 5)
                        .ToList();
                    return admins;
                }
            }
            """;

        await VerifyCS.VerifyFixAllInOnePassAsync(
            source,
            fixedSource,
            VerifyCS.Diagnostic("APB0005").WithLocation(0),
            VerifyCS.Diagnostic("APB0005").WithLocation(1));
    }

    [Fact]
    public async Task Every_trailing_comment_in_a_document_is_fixed_in_one_pass()
    {
        const string source = """
            class C
            { {|#0:// members|}
                private int _count; {|#1:// how many|}

                int M() => {|#2:// why|}
                    _count;
            }
            """;

        const string fixedSource = """
            class C
            // members
            {
                // how many
                private int _count;

                // why
                int M() =>
                    _count;
            }
            """;

        await VerifyCS.VerifyFixAllInOnePassAsync(
            source,
            fixedSource,
            VerifyCS.Diagnostic("APB0005").WithLocation(0),
            VerifyCS.Diagnostic("APB0005").WithLocation(1),
            VerifyCS.Diagnostic("APB0005").WithLocation(2));
    }

    [Fact]
    public async Task Comment_on_a_line_of_one_token_is_hoisted_above_that_token()
    {
        const string source = """
            class C
            {
                void M()
                { {|#0:// body|}
                }
            }
            """;

        const string fixedSource = """
            class C
            {
                void M()
                // body
                {
                }
            }
            """;

        await VerifyCS.VerifyCodeFixAsync(source, fixedSource, VerifyCS.Diagnostic("APB0005").WithLocation(0));
    }

    [Fact]
    public async Task Comment_on_a_collection_item_is_hoisted_inside_the_initializer()
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

        const string fixedSource = """
            class C
            {
                int[] _values =
                [
                    // first
                    1,
                    2,
                ];
            }
            """;

        await VerifyCS.VerifyCodeFixAsync(source, fixedSource, VerifyCS.Diagnostic("APB0005").WithLocation(0));
    }

    [Fact]
    public async Task Comment_on_a_documented_declaration_is_hoisted_below_the_documentation()
    {
        const string source = """
            class C
            {
                // Counted on every call.

                /// <summary>How many.</summary>
                public int Count; {|#0:// never negative|}
            }
            """;

        const string fixedSource = """
            class C
            {
                // Counted on every call.

                /// <summary>How many.</summary>
                // never negative
                public int Count;
            }
            """;

        await VerifyCS.VerifyCodeFixAsync(source, fixedSource, VerifyCS.Diagnostic("APB0005").WithLocation(0));
    }

    [Fact]
    public async Task Comment_left_after_an_expression_body_arrow_is_hoisted_above_the_member()
    {
        const string source = """
            class C
            {
                /// <summary>One.</summary>
                int M() => {|#0:// why|}
                    1;
            }
            """;

        const string fixedSource = """
            class C
            {
                /// <summary>One.</summary>
                // why
                int M() =>
                    1;
            }
            """;

        await VerifyCS.VerifyCodeFixAsync(source, fixedSource, VerifyCS.Diagnostic("APB0005").WithLocation(0));
    }

    [Fact]
    public async Task No_fix_is_offered_after_the_closing_line_of_a_multi_line_raw_string()
    {
        const string source = """"
            class C
            {
                private string _json = """
                    {}
                    """; {|#0:// empty|}
            }
            """";

        await VerifyCS.VerifyCodeFixAsync(source, source, VerifyCS.Diagnostic("APB0005").WithLocation(0));
    }

    [Fact]
    public async Task Line_endings_of_the_document_are_kept()
    {
        const string source = "class C\r\n{\r\n    private int _count; {|#0:// how many|}\r\n}\r\n";

        const string fixedSource = "class C\r\n{\r\n    // how many\r\n    private int _count;\r\n}\r\n";

        await VerifyCS.VerifyCodeFixAsync(source, fixedSource, VerifyCS.Diagnostic("APB0005").WithLocation(0));
    }
}
