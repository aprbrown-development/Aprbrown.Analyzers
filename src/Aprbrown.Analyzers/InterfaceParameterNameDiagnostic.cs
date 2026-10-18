using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aprbrown.Analyzers;

/// <summary>
/// The APB0003 diagnostic contract: what <see cref="InterfaceParameterNameAnalyzer"/> reports, and
/// what the code fix reads back from it — the parameter to rename and the name to rename it to.
/// </summary>
/// <remarks>
/// Both directions live here because each is the other's assumption. Where the diagnostic is
/// reported decides how the fixer finds the parameter again; what the diagnostic carries decides
/// what the fixer can do without repeating the analyzer's walk of
/// <c>AllInterfaces</c> / <c>FindImplementationForInterfaceMember</c>. Unlike the message
/// arguments — display text, formatted and localisable — property values survive round-tripping
/// and are addressed by name.
/// </remarks>
internal static class InterfaceParameterNameDiagnostic
{
    private const string ExpectedNameKey = "ExpectedName";

    /// <summary>
    /// The APB0003 diagnostic for <paramref name="parameter" />, which implements
    /// <paramref name="interfaceParameter" /> under a different name; <see langword="null" /> when
    /// the parameter has no source location to report at.
    /// </summary>
    /// <remarks>
    /// A parameter with no source location — an implementation inherited from metadata — is never
    /// reported. The analyzer's containing-type check is what actually excludes those, since a
    /// metadata method's containing type is never the source type under analysis; this is the
    /// guard on the reporting itself, so the rule holds where the diagnostic is made rather than
    /// only as a side effect of an earlier test.
    /// </remarks>
    public static Diagnostic? Create(IParameterSymbol parameter, IParameterSymbol interfaceParameter)
    {
        if (parameter.Locations.FirstOrDefault(location => location.IsInSource) is not { } location)
        {
            return null;
        }

        var expected = interfaceParameter.Name;
        var interfaceMethod = interfaceParameter.ContainingSymbol;

        return Diagnostic.Create(
            Descriptors.InterfaceParameterName,
            location,
            ImmutableDictionary<string, string?>.Empty.Add(ExpectedNameKey, expected),
            parameter.Name,
            expected,
            $"{interfaceMethod.ContainingType.Name}.{interfaceMethod.Name}");
    }

    /// <summary>
    /// The parameter an APB0003 diagnostic was reported on, and the name the fix renames it to;
    /// <see langword="null" /> for any diagnostic this contract does not recognise.
    /// </summary>
    /// <remarks>
    /// Unrecognised means another rule's diagnostic, one reported away from any parameter, or one
    /// without an expected name — from an older analyzer version, say. Silence beats a bad rename.
    /// </remarks>
    public static (ParameterSyntax Parameter, string ExpectedName)? Read(Diagnostic diagnostic, SyntaxNode root)
    {
        if (!string.Equals(diagnostic.Id, Descriptors.InterfaceParameterName.Id, StringComparison.Ordinal))
        {
            return null;
        }

        // Create reports on the parameter's identifier, so the enclosing declaration is one step
        // up; FirstAncestorOrSelf absorbs the difference between Roslyn handing back the
        // identifier's parent and the parameter itself.
        if (root.FindNode(diagnostic.Location.SourceSpan)?.FirstAncestorOrSelf<ParameterSyntax>() is not { } parameter)
        {
            return null;
        }

        if (!diagnostic.Properties.TryGetValue(ExpectedNameKey, out var expected)
            || string.IsNullOrEmpty(expected))
        {
            return null;
        }

        return (parameter, expected!);
    }
}
