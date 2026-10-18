using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aprbrown.Analyzers;

/// <summary>
/// APB0002 — flags a <see cref="System.Threading.CancellationToken"/> parameter that carries a
/// default value (spec §3.2), so the caller always decides cancellation. One diagnostic per
/// offending parameter, reported at the parameter.
/// </summary>
/// <remarks>
/// <para>
/// Two boundaries are enforced structurally rather than by a filter, because a filter is the
/// thing that goes wrong.
/// </para>
/// <para>
/// Scope: local functions and delegates are out of scope in v1 by decision, and changing that
/// needs a version bump under ADR-0004. Rather than match parameters everywhere and filter the
/// unwanted parents back out, this analyzer registers only for the node kinds it means to cover:
/// <see cref="SyntaxKind.MethodDeclaration"/> and <see cref="SyntaxKind.ConstructorDeclaration"/>
/// — which between them cover ordinary methods, constructors and interface members — plus the two
/// record kinds. A local function is a <see cref="LocalFunctionStatementSyntax"/> and a delegate a
/// <see cref="DelegateDeclarationSyntax"/>; neither node kind ever reaches the callback.
/// </para>
/// <para>
/// Records are covered because they are the one primary-constructor shape APB0001 endorses, so a
/// positional parameter list is the only route by which a defaulted token can reach one — miss it
/// and no rule in the set catches it. Class and struct primary constructors are deliberately not
/// covered: APB0001 already rejects that shape outright, so a second diagnostic on the same
/// construct would only be noise.
/// </para>
/// <para>
/// Inherited defaults: a parameter is exempt when its method overrides or implements a member
/// whose corresponding parameter is itself defaulted. A call site binds to the declared type, so
/// that member's default applies whatever the override says, and <c>MA0061</c> demands the
/// override keep it — reporting here would leave no edit that satisfies both rules. The default is
/// reported once, on the member that chose it. An override or implementation that introduces a
/// default its member lacks is still reported.
/// </para>
/// <para>
/// Resolvability: the token type is looked up once per compilation. In a compilation where
/// <c>System.Threading.CancellationToken</c> cannot be resolved, no callback is registered at
/// all, so the rule reports nothing instead of comparing against a null symbol.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CancellationTokenDefaultAnalyzer : DiagnosticAnalyzer
{
    private const string CancellationTokenMetadataName = "System.Threading.CancellationToken";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
        = ImmutableArray.Create(Descriptors.CancellationTokenDefault);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static compilationStart =>
        {
            // Null when the type is missing from the compilation's references, and also when it is
            // ambiguous across several of them. Either way there is nothing this rule can compare
            // against, so it declines to register and the compilation sees no APB0002 at all.
            if (compilationStart.Compilation.GetTypeByMetadataName(CancellationTokenMetadataName)
                is not { } cancellationTokenType)
            {
                return;
            }

            compilationStart.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeDeclaration(nodeContext, cancellationTokenType),
                SyntaxKind.MethodDeclaration,
                SyntaxKind.ConstructorDeclaration,
                SyntaxKind.RecordDeclaration,
                SyntaxKind.RecordStructDeclaration);
        });
    }

    private static void AnalyzeDeclaration(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol cancellationTokenType)
    {
        // A method or constructor always has a parameter list; a record's positional list is
        // optional, and "record R;" simply has none to inspect.
        var parameterList = context.Node switch
        {
            BaseMethodDeclarationSyntax method => method.ParameterList,
            RecordDeclarationSyntax record => record.ParameterList,
            _ => null,
        };

        if (parameterList is null)
        {
            return;
        }

        foreach (var parameter in parameterList.Parameters)
        {
            // The syntactic default clause is the rule's subject, whatever expression fills it, so
            // the explicit forms are not a loophole. The compiler accepts three here — "= default",
            // "= default(CancellationToken)" and "= new CancellationToken()" — and not
            // "= CancellationToken.None", since a default must be a compile-time constant.
            if (parameter.Default is null)
            {
                continue;
            }

            if (context.SemanticModel.GetDeclaredSymbol(parameter, context.CancellationToken)
                is not { } parameterSymbol)
            {
                continue;
            }

            // Exactly System.Threading.CancellationToken — a same-named type in another namespace
            // is a different type and is not this rule's business.
            if (!SymbolEqualityComparer.Default.Equals(parameterSymbol.Type, cancellationTokenType))
            {
                continue;
            }

            if (InheritsDefault(parameterSymbol))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Descriptors.CancellationTokenDefault,
                parameter.GetLocation(),
                parameterSymbol.Name));
        }
    }

    private static bool InheritsDefault(IParameterSymbol parameter)
    {
        if (parameter.ContainingSymbol is not IMethodSymbol method)
        {
            return false;
        }

        // Only the member directly overridden: if a middle override dropped the default, a call
        // through it binds without one, and re-adding it further down is introducing a default.
        if (HasDefaultAt(method.OverriddenMethod, parameter.Ordinal))
        {
            return true;
        }

        return InterfaceImplementations.Of(method).Any(member => HasDefaultAt(member, parameter.Ordinal));
    }

    // An override or implementation always has the arity of its member. The bounds check is for
    // half-typed code in the IDE, where error recovery can hand back a pair that does not agree,
    // and an out-of-range index would take the whole analysis down — APB0003 clamps for the same.
    private static bool HasDefaultAt(IMethodSymbol? member, int ordinal) =>
        member is not null
            && ordinal < member.Parameters.Length
            && member.Parameters[ordinal].HasExplicitDefaultValue;
}
