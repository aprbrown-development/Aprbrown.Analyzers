using Microsoft.CodeAnalysis;

namespace Aprbrown.Analyzers;

/// <summary>
/// Every analyzer's <see cref="DiagnosticDescriptor"/> in one shared place (README "Adding an
/// analyzer", step 1), so a rule's public identity — ID, category, default severity, message —
/// reads as one table as more rules land.
/// </summary>
internal static class Descriptors
{
    private const string HelpLinkUri = "https://github.com/aprbrown-development/Aprbrown.Analyzers";

    /// <summary>APB0001 — do not use primary constructors on classes or structs (spec §3.1).</summary>
    public static readonly DiagnosticDescriptor PrimaryConstructor = new(
        id: DiagnosticIds.PrimaryConstructor,
        title: "Do not use primary constructors on classes or structs",
        messageFormat: "'{0}' declares a primary constructor; use an explicit constructor with readonly fields",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Injected dependencies belong in an explicit constructor assigning readonly fields, "
            + "so a class's dependency surface is one legible block rather than a parameter list the "
            + "compiler scatters.",
        helpLinkUri: HelpLinkUri);

    /// <summary>APB0002 — do not give a CancellationToken parameter a default value (spec §3.2).</summary>
    public static readonly DiagnosticDescriptor CancellationTokenDefault = new(
        id: DiagnosticIds.CancellationTokenDefault,
        title: "Do not give a CancellationToken parameter a default value",
        messageFormat: "Parameter '{0}' defaults its CancellationToken; make every caller pass one",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The caller always decides cancellation. A defaulted token lets a caller "
            + "silently opt out, and the opting-out is invisible at the call site. CA2016, MA0032 "
            + "and MA0040 police the call site; this rule is the declaration half.",
        helpLinkUri: HelpLinkUri);
}
