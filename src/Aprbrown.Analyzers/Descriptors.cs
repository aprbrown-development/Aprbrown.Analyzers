using Microsoft.CodeAnalysis;

namespace Aprbrown.Analyzers;

/// <summary>
/// Every analyzer's <see cref="DiagnosticDescriptor"/> in one shared place, so a rule's public
/// identity — ID, category, default severity, message — reads as one table as more rules land.
/// </summary>
/// <remarks>
/// Diagnostic IDs are public API: consumers reference them by string in configuration
/// they own, so a rename is a breaking change. Each is written once, here; the code fix reads it back
/// through the descriptor's <see cref="DiagnosticDescriptor.Id"/>.
/// </remarks>
internal static class Descriptors
{
    /// <summary>APB0001 — do not use primary constructors on classes or structs.</summary>
    public static readonly DiagnosticDescriptor PrimaryConstructor = new(
        id: "APB0001",
        title: "Do not use primary constructors on classes or structs",
        messageFormat: "'{0}' declares a primary constructor; use an explicit constructor with readonly fields",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Injected dependencies belong in an explicit constructor assigning readonly fields, "
            + "so a class's dependency surface is one legible block rather than a parameter list the "
            + "compiler scatters.",
        helpLinkUri: HelpLinkUri);

    /// <summary>APB0002 — do not give a CancellationToken parameter a default value.</summary>
    public static readonly DiagnosticDescriptor CancellationTokenDefault = new(
        id: "APB0002",
        title: "Do not give a CancellationToken parameter a default value",
        messageFormat: "Parameter '{0}' defaults its CancellationToken; make every caller pass one",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The caller always decides cancellation. A defaulted token lets a caller "
            + "silently opt out, and the opting-out is invisible at the call site. MA0032 polices "
            + "the call site; this rule is the declaration half. An override or implementation that "
            + "keeps a default its base or interface member already declares is exempt.",
        helpLinkUri: HelpLinkUri);

    /// <summary>APB0003 — parameter names should match the implemented interface member.</summary>
    public static readonly DiagnosticDescriptor InterfaceParameterName = new(
        id: "APB0003",
        title: "Parameter names should match the implemented interface member",
        messageFormat: "Parameter '{0}' should be named '{1}' to match {2}",
        category: "Naming",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An interface's parameter names survive into every type that implements it. "
            + "CA1725 reports some interface implementations too, but accepts a match with any one "
            + "of several interfaces; this package suppresses CA1725 there (APBS0001), so this "
            + "stricter rule is the single report. CA1725 still covers base-class overrides.",
        helpLinkUri: HelpLinkUri);

    /// <summary>APB0004 — put an expression body's arrow at the end of the declaration line.</summary>
    public static readonly DiagnosticDescriptor ExpressionBodyArrow = new(
        id: "APB0004",
        title: "Put an expression body's arrow at the end of the declaration line",
        messageFormat: "Move '=>' to the end of the previous line",
        category: "Style",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A wrapped expression-bodied member ends its declaration line with '=>' and puts "
            + "the expression on the next line, so the line that declares the member also says it "
            + "has an expression body. Lambdas and switch-expression arms are not expression bodies "
            + "and are not covered.",
        helpLinkUri: HelpLinkUri);

    /// <summary>APB0005 — put a comment on its own line above the code it describes.</summary>
    public static readonly DiagnosticDescriptor TrailingComment = new(
        id: "APB0005",
        title: "Put a comment on its own line above the code it describes",
        messageFormat: "Move this comment onto its own line above the code",
        category: "Style",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A comment explains the code below it. One trailing a line of code is read after "
            + "the code it was meant to explain, and is pushed out of sight by long lines. Block "
            + "comments, documentation, comments on preprocessor directives and tool directives such "
            + "as NOSONAR, ReSharper and lang= are exempt.",
        helpLinkUri: HelpLinkUri);

    /// <summary>
    /// APBS0001 — suppresses CA1725 on an interface implementation, where APB0003 is the rule that
    /// reports a renamed parameter.
    /// </summary>
    public static readonly SuppressionDescriptor InterfaceParameterNameSuppression = new(
        id: "APBS0001",
        suppressedDiagnosticId: "CA1725",
        justification: "APB0003 reports a parameter renamed from the interface member it implements; "
            + "CA1725 would report it a second time.");

    // Below the descriptors rather than above them, because SA1202 orders members by access before
    // it considers const-ness and this is the only private member of the type.
    //
    // This is what an IDE opens when a consumer clicks APB000n on a squiggle, so it points at the
    // section that explains the custom rules rather than at the repository root — consuming.md is
    // the one place a consumer learns what a rule does. Deep-linked to a heading rather
    // than per-rule: they are one short list, and one anchor cannot drift out of step with several.
    private const string HelpLinkUri =
        "https://github.com/aprbrown-development/Aprbrown.Analyzers/blob/main/docs/consuming.md#the-three-custom-rules";
}
