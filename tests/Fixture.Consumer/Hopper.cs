using System.Collections.Concurrent;

namespace Fixture.Consumer;

// The performance tier's proof. Both rules below default to Info, so each firing at warning shows
// the shipped config raising a rule's severity, one rule per vendor.
public sealed class Hopper
{
    private readonly ConcurrentDictionary<int, int[]> _bins = new();

    public static bool IsLabelled(string name) => name.Contains("#"); // expect: CA1847 warning

    public int[] Bin(int size) => _bins.GetOrAdd(size, new int[size]); // expect: MA0242 warning
}
