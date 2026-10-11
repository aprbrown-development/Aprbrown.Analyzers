using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Aprbrown.Analyzers.Tests;

/// <summary>
/// The APB0003 diagnostic contract, tested through its own round trip: what
/// <see cref="InterfaceParameterNameDiagnostic.Create"/> reports,
/// <see cref="InterfaceParameterNameDiagnostic.Read"/> gives back, and anything it does not
/// recognise reads as nothing. The analyzer and fixer tests cover each side as the IDE sees it;
/// this covers the paths they cannot reach, such as a diagnostic from an older analyzer that
/// carries no expected name.
/// </summary>
public sealed class InterfaceParameterNameDiagnosticTests
{
    [Fact]
    public void A_created_diagnostic_reads_back_as_the_parameter_and_the_interfaces_name()
    {
        var (parameter, interfaceParameter, root) = Mismatch();

        var contract = Assert.NotNull(InterfaceParameterNameDiagnostic.Read(Created(parameter, interfaceParameter), root));

        Assert.Same(parameter.DeclaringSyntaxReferences.Single().GetSyntax(CancellationToken.None), contract.Parameter);
        Assert.Equal("alpha", contract.ExpectedName);
    }

    [Fact]
    public void A_diagnostic_without_the_expected_name_reads_as_nothing()
    {
        // An analyzer from before the name travelled with the diagnostic, say. Silence beats a
        // fixer guessing at a rename.
        var (parameter, interfaceParameter, root) = Mismatch();
        var created = Created(parameter, interfaceParameter);

        var stripped = Recreated(created, properties: ImmutableDictionary<string, string?>.Empty);

        Assert.Null(InterfaceParameterNameDiagnostic.Read(stripped, root));
    }

    [Fact]
    public void A_diagnostic_with_an_empty_expected_name_reads_as_nothing()
    {
        // Renaming to an empty name is never a fix.
        var (parameter, interfaceParameter, root) = Mismatch();
        var created = Created(parameter, interfaceParameter);

        var blanked = Recreated(
            created,
            properties: created.Properties.ToImmutableDictionary(property => property.Key, _ => (string?)string.Empty));

        Assert.Null(InterfaceParameterNameDiagnostic.Read(blanked, root));
    }

    [Fact]
    public void Another_rules_diagnostic_reads_as_nothing()
    {
        // Even one carrying the same properties at the same place: the contract belongs to APB0003.
        var (parameter, interfaceParameter, root) = Mismatch();
        var created = Created(parameter, interfaceParameter);

        var foreign = Recreated(created, descriptor: Descriptors.CancellationTokenDefault);

        Assert.Null(InterfaceParameterNameDiagnostic.Read(foreign, root));
    }

    [Fact]
    public void A_diagnostic_reported_away_from_any_parameter_reads_as_nothing()
    {
        // The interface keyword opening the source: a span with no parameter around it.
        var (parameter, interfaceParameter, root) = Mismatch();
        var created = Created(parameter, interfaceParameter);

        var misplaced = Recreated(created, location: Location.Create(root.SyntaxTree, new TextSpan(0, "interface".Length)));

        Assert.Null(InterfaceParameterNameDiagnostic.Read(misplaced, root));
    }

    [Fact]
    public void A_parameter_declared_only_in_metadata_is_not_reported()
    {
        // Spec §3.3: a parameter with no source location is never reported. Int32.Equals — the
        // interface parameter's own type, read from the referenced corlib — stands in for any
        // implementation the compiler knows only from metadata.
        var (_, interfaceParameter, _) = Mismatch();
        var equals = interfaceParameter.Type.GetMembers(nameof(Equals)).OfType<IMethodSymbol>().First();

        Assert.Null(InterfaceParameterNameDiagnostic.Create(equals.Parameters.Single(), interfaceParameter));
    }

    /// <summary>
    /// <paramref name="diagnostic" /> as another reporter might have raised it, with whichever of
    /// its descriptor, location and properties are given replaced.
    /// </summary>
    /// <remarks>
    /// The message arguments are fixed rather than carried over — a diagnostic does not expose
    /// them — and <see cref="InterfaceParameterNameDiagnostic.Read"/> never looks at the message.
    /// </remarks>
    private static Diagnostic Recreated(
        Diagnostic diagnostic,
        DiagnosticDescriptor? descriptor = null,
        Location? location = null,
        ImmutableDictionary<string, string?>? properties = null)
        => Diagnostic.Create(
            descriptor ?? diagnostic.Descriptor,
            location ?? diagnostic.Location,
            properties ?? diagnostic.Properties,
            "beta",
            "alpha",
            "I.M");

    private static Diagnostic Created(IParameterSymbol parameter, IParameterSymbol interfaceParameter)
    {
        var diagnostic = InterfaceParameterNameDiagnostic.Create(parameter, interfaceParameter);
        Assert.NotNull(diagnostic);
        return diagnostic;
    }

    /// <summary>
    /// A class implementing an interface with a differently named parameter: the implementing
    /// parameter <c>beta</c>, the interface's <c>alpha</c>, and the root of the tree declaring both.
    /// </summary>
    private static (IParameterSymbol Parameter, IParameterSymbol InterfaceParameter, SyntaxNode Root) Mismatch()
    {
        var tree = CSharpSyntaxTree.ParseText(
            """
            interface I { void M(int alpha); }
            class C : I { public void M(int beta) { } }
            """,
            cancellationToken: CancellationToken.None);
        var compilation = CSharpCompilation.Create(
            assemblyName: "Contract",
            syntaxTrees: [tree],
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        return (
            OnlyParameterOfM(compilation.GetTypeByMetadataName("C")),
            OnlyParameterOfM(compilation.GetTypeByMetadataName("I")),
            tree.GetRoot(CancellationToken.None));
    }

    private static IParameterSymbol OnlyParameterOfM(INamedTypeSymbol? type)
    {
        Assert.NotNull(type);
        return Assert.IsAssignableFrom<IMethodSymbol>(type.GetMembers("M").Single()).Parameters.Single();
    }
}
