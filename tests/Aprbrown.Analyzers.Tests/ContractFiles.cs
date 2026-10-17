using System.Text.RegularExpressions;

namespace Aprbrown.Analyzers.Tests;

/// <summary>
/// The files the contract tests hold the code to, copied beside the test assembly under
/// <c>Contract/</c> by the project file.
/// </summary>
internal static partial class ContractFiles
{
    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contract", name));

    // Every `dotnet_diagnostic.<id>.severity = <value>` line of a config file, by ID. Section headers
    // are not tracked, so an ID set in two sections is refused at ToDictionary rather than resolved.
    public static Dictionary<string, string> Severities(string name) =>
        Read(name)
            .Split('\n')
            .Select(line => SeverityLine().Match(line))
            .Where(m => m.Success)
            .ToDictionary(m => m.Groups["id"].Value, m => m.Groups["severity"].Value, StringComparer.Ordinal);

    [GeneratedRegex(@"^dotnet_diagnostic\.(?<id>[^.\s]+)\.severity[ \t]*=[ \t]*(?<severity>[a-z]+)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SeverityLine();
}
