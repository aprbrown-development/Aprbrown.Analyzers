using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Aprbrown.Analyzers.Tests;

/// <summary>
/// The descriptor contract test: every rule this package defines, found through the analyzers'
/// public <see cref="DiagnosticAnalyzer.SupportedDiagnostics"/>, is held to what a consumer is told
/// about it. A rule added later is covered without this class being edited.
/// </summary>
public sealed partial class DescriptorContractTests
{
    public static TheoryData<string> DescriptorIds => new(AnalyzerDescriptors().Select(d => d.Id));

    // A help link is what an IDE opens when a consumer clicks the rule on a squiggle. GitHub serves
    // the page and ignores an unknown fragment, so a reworded heading leaves no 404 to notice: the
    // consumer lands at the top of a long document with no sign they were meant to be further down.
    [Theory]
    [MemberData(nameof(DescriptorIds))]
    public void Help_link_lands_on_a_heading_of_the_consuming_guide(string id)
    {
        var link = Descriptor(id).HelpLinkUri;
        var match = GitHubFileLink().Match(link);

        Assert.True(
            match.Success,
            $"{id}'s help link '{link}' is not a #fragment of a file on GitHub at a full commit hash; a link "
            + "without a fragment lands a consumer at the top of the document rather than at the rule, and "
            + "one at a branch drifts from the version the consumer has.");
        Assert.Equal(ConsumingGuidePath(), match.Groups["path"].Value);

        var fragment = match.Groups["fragment"].Value;
        var headings = MarkdownHeadings.Slugs(ContractFiles.Read("consuming.md"));
        Assert.True(
            headings.Contains(fragment, StringComparer.Ordinal),
            $"{id}'s help link fragment '#{fragment}' matches no heading in {ConsumingGuidePath()}. Either "
            + "restore the heading or point the link at one that is there. Headings: "
            + string.Join(", ", headings));
    }

    // The shipped config's blanket overrides every default, so a consumer sees the config's severity
    // whatever the descriptor says. Holding the two equal keeps the analyzer tests — which verify at
    // the descriptor's DefaultSeverity — verifying at the severity a consumer actually gets.
    [Theory]
    [MemberData(nameof(DescriptorIds))]
    public void Shipped_config_sets_the_descriptors_default_severity(string id)
    {
        var expected = Descriptor(id).DefaultSeverity switch
        {
            DiagnosticSeverity.Error => "error",
            DiagnosticSeverity.Warning => "warning",
            DiagnosticSeverity.Info => "suggestion",
            _ => "silent",
        };

        Assert.True(
            ShippedSeverities().TryGetValue(id, out var shipped),
            $"{id} has no dotnet_diagnostic.{id}.severity line in the shipped config, so the blanket leaves it off.");
        Assert.True(
            string.Equals(shipped, expected, StringComparison.Ordinal),
            $"The shipped config sets {id} to '{shipped}' but its descriptor's DefaultSeverity is '{expected}'. "
            + "Change one to match the other.");
    }

    // Both directions at once. A descriptor with no config line is off under the blanket; a config
    // line with no descriptor is a retired rule still configured, which does nothing. Non-empty so a
    // scan that found no analyzers fails here rather than passing every theory vacuously.
    [Fact]
    public void Shipped_config_names_exactly_the_rules_the_package_defines()
    {
        var defined = AnalyzerDescriptors().Select(d => d.Id).Order(StringComparer.Ordinal).ToList();
        var configured = ShippedSeverities().Keys
            .Where(id => id.StartsWith("APB", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(defined);
        Assert.Equal(defined, configured);
    }

    private static DiagnosticDescriptor Descriptor(string id) =>
        AnalyzerDescriptors().Single(d => string.Equals(d.Id, id, StringComparison.Ordinal));

    // Every concrete analyzer the package ships, read through the interface a host reads it through.
    // Deduplicated by the descriptor's own value equality, not by ID: one descriptor shared by two
    // analyzers is one rule, but two that claim the same ID and differ are a conflict, which
    // Descriptor(id) refuses at Single.
    private static IEnumerable<DiagnosticDescriptor> AnalyzerDescriptors() =>
        typeof(PrimaryConstructorAnalyzer).Assembly.GetTypes()
            .Where(t => !t.IsAbstract
                && typeof(DiagnosticAnalyzer).IsAssignableFrom(t)
                && Attribute.IsDefined(t, typeof(DiagnosticAnalyzerAttribute)))
            .SelectMany(t => ((DiagnosticAnalyzer)Activator.CreateInstance(t)!).SupportedDiagnostics)
            .Distinct();

    private static Dictionary<string, string> ShippedSeverities() =>
        ContractFiles.Severities("Aprbrown.Analyzers.globalconfig");

    private static string ConsumingGuidePath() =>
        typeof(DescriptorContractTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => string.Equals(a.Key, "ConsumingGuidePath", StringComparison.Ordinal))
            .Value!;

    [GeneratedRegex(@"^https://github\.com/[^/]+/[^/]+/blob/[0-9a-f]{40}/(?<path>[^#]+)#(?<fragment>.+)$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex GitHubFileLink();
}
