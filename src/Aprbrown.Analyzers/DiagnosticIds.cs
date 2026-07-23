namespace Aprbrown.Analyzers;

/// <summary>
/// Diagnostic IDs are public API (ADR-0004): consumers reference them by string in
/// configuration they own, so a rename is a breaking change.
/// </summary>
internal static class DiagnosticIds
{
    /// <summary>APB0001 — do not use primary constructors on classes or structs.</summary>
    internal const string PrimaryConstructor = "APB0001";
}
