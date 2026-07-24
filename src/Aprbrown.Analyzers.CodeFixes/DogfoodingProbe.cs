namespace Aprbrown.Analyzers.CodeFixes;

/// <summary>
/// Deliberate APB0001 violation — a temporary probe proving the dogfooding gate is live in CI.
/// Reverted in the following commit.
/// </summary>
internal sealed class DogfoodingProbe(int value)
{
    /// <summary>Gets the probe value.</summary>
    public int Value => value;
}
