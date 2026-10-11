using Xunit;

namespace Aprbrown.Analyzers.Tests;

/// <summary>
/// The slugs the descriptor contract test matches help-link fragments against. Expected values are
/// the anchors GitHub renders for these headings, not a recomputation of the slug rules.
/// </summary>
public sealed class MarkdownHeadingsTests
{
    [Fact]
    public void Heading_becomes_its_lowercase_hyphenated_slug()
    {
        Assert.Equal(["the-three-custom-rules"], MarkdownHeadings.Slugs("### The three custom rules\n"));
    }

    [Fact]
    public void Punctuation_is_dropped_but_hyphens_and_underscores_survive()
    {
        const string markdown = """
            ### ReSharper offers the primary constructor `APB0001` forbids
            ### 1. Reference three packages
            ## snake_case and kebab-case
            """;

        Assert.Equal(
            ["resharper-offers-the-primary-constructor-apb0001-forbids", "1-reference-three-packages", "snake_case-and-kebab-case"],
            MarkdownHeadings.Slugs(markdown));
    }

    [Fact]
    public void Comment_lines_in_a_fenced_block_are_not_headings()
    {
        // consuming.md is largely .editorconfig samples, whose comments begin with '#'. Admitted as
        // headings, they would be extra slugs for a broken fragment to match by accident.
        const string markdown = """
            ## Before
            ```ini
            # Required if you have tests
            ```
            ~~~
            # Also not a heading
            ~~~
            ## After
            """;

        Assert.Equal(["before", "after"], MarkdownHeadings.Slugs(markdown));
    }

    [Fact]
    public void A_repeated_heading_takes_a_numbered_suffix()
    {
        const string markdown = """
            ## Examples
            ## Examples
            ## Examples
            """;

        Assert.Equal(["examples", "examples-1", "examples-2"], MarkdownHeadings.Slugs(markdown));
    }

    [Fact]
    public void Closing_hashes_trailing_space_and_carriage_returns_are_not_part_of_the_slug()
    {
        Assert.Equal(
            ["versioning", "links"],
            MarkdownHeadings.Slugs("## Versioning ##\r\n## Links   \r\n"));
    }
}
