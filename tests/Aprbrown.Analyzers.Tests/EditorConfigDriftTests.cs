using Xunit;

namespace Aprbrown.Analyzers.Tests;

/// <summary>
/// The repository's .editorconfig restates the shipped config's expression-bodied severities, because
/// <c>dotnet format</c> ignores a severity delivered through a global config. The .editorconfig
/// outranks the global config, so a severity changed in one and not the other would be silently
/// overridden in this repository; this holds the two in step.
/// </summary>
public sealed class EditorConfigDriftTests
{
    // Only IDE rules are restatements. The .editorconfig's other severity lines are this repository's
    // deviations, which differ from the shipped config on purpose. Non-empty so that a restated block
    // the parse no longer finds fails here rather than passing vacuously.
    [Fact]
    public void Every_restated_IDE_severity_matches_the_shipped_config()
    {
        var shipped = ContractFiles.Severities("Aprbrown.Analyzers.globalconfig");
        var restated = ContractFiles.Severities("repository.editorconfig")
            .Where(entry => entry.Key.StartsWith("IDE", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(restated);

        var drifted = restated
            .Where(entry => !shipped.TryGetValue(entry.Key, out var severity)
                || !string.Equals(severity, entry.Value, StringComparison.Ordinal))
            .Select(entry => $"{entry.Key}: .editorconfig '{entry.Value}', shipped config '"
                + (shipped.TryGetValue(entry.Key, out var severity) ? severity : "not set") + "'")
            .ToList();

        Assert.True(
            drifted.Count == 0,
            "The repository's .editorconfig restates severities that disagree with the shipped config: "
            + string.Join("; ", drifted)
            + ". Change the .editorconfig to match, or delete the line if the rule is no longer shipped.");
    }
}
