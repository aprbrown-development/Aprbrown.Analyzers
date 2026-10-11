using System.Text;
using System.Text.RegularExpressions;

namespace Aprbrown.Analyzers.Tests;

/// <summary>
/// The anchors GitHub gives the ATX headings of a Markdown document — what a help link's
/// <c>#fragment</c> must name to land on its heading rather than at the top of the page.
/// </summary>
internal static partial class MarkdownHeadings
{
    public static IReadOnlyList<string> Slugs(string markdown)
    {
        var slugs = new List<string>();
        var timesSeen = new Dictionary<string, int>(StringComparer.Ordinal);
        string? openFence = null;
        foreach (var line in markdown.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            var fence = Fence().Match(line);
            if (fence.Success && (openFence is null || fence.Value.TrimStart().StartsWith(openFence, StringComparison.Ordinal)))
            {
                openFence = openFence is null ? fence.Value.TrimStart() : null;
                continue;
            }

            if (openFence is not null)
            {
                continue;
            }

            var heading = AtxHeading().Match(line);
            if (heading.Success)
            {
                // GitHub numbers a repeated slug by how many times it has been seen before.
                var slug = Slug(heading.Groups["text"].Value);
                var seen = timesSeen.GetValueOrDefault(slug);
                timesSeen[slug] = seen + 1;
                slugs.Add(seen == 0 ? slug : $"{slug}-{seen}");
            }
        }

        return slugs;
    }

    // GitHub's rule for an ASCII heading: lowercase, drop anything that is not alphanumeric, space,
    // hyphen or underscore — backticks and periods go — then spaces to hyphens. Accented letters,
    // which GitHub keeps, are dropped; that fails a link that in fact works, the harmless direction.
    private static string Slug(string text)
    {
        var slug = new StringBuilder(text.Length);
        foreach (var c in text.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
            {
                slug.Append(c);
            }
            else if (c == ' ')
            {
                slug.Append('-');
            }
        }

        return slug.ToString();
    }

    [GeneratedRegex(@"^ {0,3}#{1,6}[ \t]+(?<text>.*?)([ \t]+#+)?[ \t]*$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AtxHeading();

    // The opening run alone, so a closing fence is recognised by the same character it opened with.
    [GeneratedRegex(@"^ {0,3}(```+|~~~+)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Fence();
}
