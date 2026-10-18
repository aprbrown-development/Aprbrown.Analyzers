using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Aprbrown.Analyzers;

/// <summary>
/// Finds the interface members a method implements on its containing type.
/// </summary>
internal static class InterfaceImplementations
{
    /// <summary>
    /// The interface members <paramref name="method"/> implements: those it names explicitly, or
    /// otherwise every member of its containing type's interfaces it implicitly satisfies. Empty
    /// for a method that implements nothing.
    /// </summary>
    public static IEnumerable<IMethodSymbol> Of(IMethodSymbol method)
    {
        // An explicit implementation names its interface member outright. Its own name is the
        // qualified "I.M", so the name lookup below would never find it anyway.
        if (!method.ExplicitInterfaceImplementations.IsEmpty)
        {
            return method.ExplicitInterfaceImplementations;
        }

        return ImplicitlyImplemented(method);
    }

    private static IEnumerable<IMethodSymbol> ImplicitlyImplemented(IMethodSymbol method)
    {
        // An implicit implementation carries no back-pointer to the interface member it satisfies,
        // so the question is asked from the type, as APB0003 asks it.
        var type = method.ContainingType;

        foreach (var @interface in type.AllInterfaces)
        {
            foreach (var member in @interface.GetMembers(method.Name))
            {
                if (member is IMethodSymbol interfaceMethod
                    && SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(interfaceMethod), method))
                {
                    yield return interfaceMethod;
                }
            }
        }
    }
}
