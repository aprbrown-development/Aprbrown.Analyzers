using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aprbrown.Analyzers;

/// <summary>
/// APBS0001 — suppresses <c>CA1725</c> on a parameter of a method that implements an interface
/// member, implicitly or explicitly, so <see cref="InterfaceParameterNameAnalyzer"/> (APB0003) is the
/// one rule reporting a renamed interface parameter. A parameter renamed from the base member its
/// method overrides keeps its <c>CA1725</c>, whatever interfaces the method also implements.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InterfaceParameterNameSuppressor : DiagnosticSuppressor
{
    /// <inheritdoc />
    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions { get; }
        = ImmutableArray.Create(Descriptors.InterfaceParameterNameSuppression);

    /// <inheritdoc />
    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            if (ReportedParameter(context, diagnostic) is { ContainingSymbol: IMethodSymbol method } parameter
                && InterfaceImplementations.Of(method).Any()
                && !RenamesOverriddenParameter(method, parameter))
            {
                context.ReportSuppression(Suppression.Create(Descriptors.InterfaceParameterNameSuppression, diagnostic));
            }
        }
    }

    private static bool RenamesOverriddenParameter(IMethodSymbol method, IParameterSymbol parameter) =>
        method.OverriddenMethod is { } overridden
        && parameter.Ordinal < overridden.Parameters.Length
        && !string.Equals(overridden.Parameters[parameter.Ordinal].Name, parameter.Name, StringComparison.Ordinal);

    private static IParameterSymbol? ReportedParameter(SuppressionAnalysisContext context, Diagnostic diagnostic)
    {
        if (diagnostic.Location.SourceTree is not { } tree)
        {
            return null;
        }

        var node = tree.GetRoot(context.CancellationToken).FindNode(diagnostic.Location.SourceSpan);

        return context.GetSemanticModel(tree).GetDeclaredSymbol(node, context.CancellationToken) as IParameterSymbol;
    }
}
