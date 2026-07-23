using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aprbrown.Analyzers;

/// <summary>
/// APB0003 — flags an interface implementation whose parameter names differ from the interface's
/// (spec §3.3). <c>CA1725</c> polices base-class overrides and is blind to interface
/// implementations; this is the missing half.
/// </summary>
/// <remarks>
/// Driven from the named type rather than the method, because the question "does this method
/// implement an interface member, and which one" is only answerable from the type: a method
/// carries no back-pointer to the interface member it satisfies. Walking
/// <see cref="ITypeSymbol.AllInterfaces"/> and asking
/// <see cref="ITypeSymbol.FindImplementationForInterfaceMember"/> also settles the inheritance
/// case for free — the implementation it hands back for a member satisfied by a base type
/// belongs to that base, so the containing-type check below excludes it without a separate
/// hierarchy walk.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InterfaceParameterNameAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
        = ImmutableArray.Create(Descriptors.InterfaceParameterName);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        // Only implementers are policed. An interface — including one carrying a default
        // implementation of an inherited member — is a declaration, not an implementation.
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
        {
            return;
        }

        foreach (var @interface in type.AllInterfaces)
        {
            foreach (var member in @interface.GetMembers())
            {
                // Ordinary methods only. Property and event accessors reach here as
                // MethodKind.PropertySet / EventAdd and the like, and their parameters are
                // synthesised by the compiler — there is no author-written name to rename.
                if (member is not IMethodSymbol { MethodKind: MethodKind.Ordinary } interfaceMethod)
                {
                    continue;
                }

                AnalyzeImplementation(context, type, interfaceMethod);
            }
        }
    }

    private static void AnalyzeImplementation(
        SymbolAnalysisContext context,
        INamedTypeSymbol type,
        IMethodSymbol interfaceMethod)
    {
        if (type.FindImplementationForInterfaceMember(interfaceMethod) is not IMethodSymbol implementation)
        {
            return;
        }

        // The implementing method must be declared on this type. When a base type satisfies the
        // member, the method is the base's to answer for, and reporting it here would point at
        // code the developer cannot edit from the derived declaration.
        if (!SymbolEqualityComparer.Default.Equals(implementation.ContainingType, type))
        {
            return;
        }

        // An implementation always has the arity of the member it implements. The clamp is not
        // for that: an analyzer also runs against half-typed code in the IDE, where error
        // recovery can hand back a symbol pair that does not agree, and an out-of-range index
        // there would take the whole analysis down.
        var parameterCount = Math.Min(implementation.Parameters.Length, interfaceMethod.Parameters.Length);

        for (var i = 0; i < parameterCount; i++)
        {
            var parameter = implementation.Parameters[i];
            var expected = interfaceMethod.Parameters[i].Name;

            if (string.Equals(parameter.Name, expected, StringComparison.Ordinal))
            {
                continue;
            }

            // Spec §3.3 requires that a parameter with no source location — an implementation
            // inherited from metadata — is never reported. The containing-type check above is
            // what actually excludes those, since a metadata method's containing type is never
            // the source type under analysis. This stays as the guard on the reporting itself:
            // Diagnostic.Create needs a location, and the requirement should be enforced where
            // it is stated rather than only as a side effect of an earlier test.
            if (parameter.Locations.FirstOrDefault(location => location.IsInSource) is not { } location)
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Descriptors.InterfaceParameterName,
                location,
                parameter.Name,
                expected,
                $"{interfaceMethod.ContainingType.Name}.{interfaceMethod.Name}"));
        }
    }
}
