using Xunit;
using VerifyCS = Aprbrown.Analyzers.Tests.CSharpAnalyzerVerifier<Aprbrown.Analyzers.PrimaryConstructorAnalyzer>;

namespace Aprbrown.Analyzers.Tests;

/// <summary>
/// Covers every required-test row for APB0001 (spec §3.1). The record cases are the primary
/// test, not edge cases: a wrong filter on the shared syntax base flags every record.
/// </summary>
public sealed class PrimaryConstructorAnalyzerTests
{
    [Fact]
    public async Task Class_with_primary_constructor_is_flagged_at_the_parameter_list()
    {
        const string source = "class C{|#0:(int x)|} { }";

        await VerifyCS.VerifyAnalyzerAsync(
            source,
            VerifyCS.Diagnostic("APB0001").WithLocation(0).WithArguments("C"));
    }

    [Fact]
    public async Task Struct_with_primary_constructor_is_flagged()
    {
        const string source = "struct S{|#0:(int x)|} { }";

        await VerifyCS.VerifyAnalyzerAsync(
            source,
            VerifyCS.Diagnostic("APB0001").WithLocation(0).WithArguments("S"));
    }

    [Fact]
    public async Task Record_is_never_flagged()
    {
        // 537 positional records fleet-wide must stay silent.
        await VerifyCS.VerifyAnalyzerAsync("record R(int X);");
    }

    [Fact]
    public async Task Record_struct_is_never_flagged()
    {
        await VerifyCS.VerifyAnalyzerAsync("record struct RS(int X);");
    }

    [Fact]
    public async Task Class_with_explicit_constructor_is_not_flagged()
    {
        await VerifyCS.VerifyAnalyzerAsync("class C { public C(int x) { } }");
    }

    [Fact]
    public async Task Generic_class_with_primary_constructor_is_flagged()
    {
        const string source = "class C<T>{|#0:(T value)|} { }";

        await VerifyCS.VerifyAnalyzerAsync(
            source,
            VerifyCS.Diagnostic("APB0001").WithLocation(0).WithArguments("C"));
    }

    [Fact]
    public async Task Class_with_no_parameter_list_is_not_flagged()
    {
        await VerifyCS.VerifyAnalyzerAsync("class C { }");
    }
}
