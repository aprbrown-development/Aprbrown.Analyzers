using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aprbrown.Analyzers;

/// <summary>
/// APB0001 — flags a primary constructor on a <c>class</c> or <c>struct</c> declaration
/// (spec §3.1). Records are exempt: positional parameters are the point of a record, and
/// the fleet holds 537 of them that must stay silent.
/// </summary>
/// <remarks>
/// The record exemption is structural, not a filter. A record declaration also carries a
/// parameter list and shares the <see cref="TypeDeclarationSyntax"/> base with classes and
/// structs, so an implementation that reaches for that base and then filters flags every
/// record if the filter is wrong. This analyzer instead registers only for the
/// <see cref="SyntaxKind.ClassDeclaration"/> and <see cref="SyntaxKind.StructDeclaration"/>
/// node kinds; a <c>record</c> / <c>record struct</c> is a
/// <see cref="RecordDeclarationSyntax"/> of a different kind and never reaches the callback.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PrimaryConstructorAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
        = ImmutableArray.Create(Descriptors.PrimaryConstructor);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            AnalyzeTypeDeclaration,
            SyntaxKind.ClassDeclaration,
            SyntaxKind.StructDeclaration);
    }

    private static void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext context)
    {
        var declaration = (TypeDeclarationSyntax)context.Node;

        // No parameter list means no primary constructor. An explicit constructor is a
        // member, not a parameter list on the type, so it is not reported here either.
        if (declaration.ParameterList is not { } parameterList)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.PrimaryConstructor,
            parameterList.GetLocation(),
            declaration.Identifier.Text));
    }
}
