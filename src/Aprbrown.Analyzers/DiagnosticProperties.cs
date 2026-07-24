namespace Aprbrown.Analyzers;

/// <summary>
/// Keys for the property bag an analyzer attaches to a reported diagnostic, read back by the
/// matching code fix.
/// </summary>
/// <remarks>
/// The bag is how a fixer inherits a conclusion the analyzer already reached, rather than
/// recomputing it from the syntax it is handed. Unlike the message arguments — which are display
/// text, formatted and localisable — these values survive round-tripping and are addressed by name.
/// </remarks>
internal static class DiagnosticProperties
{
    /// <summary>
    /// APB0003 — the name the interface gives the parameter, which the fix renames it to. Without
    /// it the fixer would have to repeat the analyzer's walk of
    /// <c>AllInterfaces</c> / <c>FindImplementationForInterfaceMember</c> to answer a question the
    /// analyzer answered a moment earlier.
    /// </summary>
    internal const string ExpectedName = "ExpectedName";
}
