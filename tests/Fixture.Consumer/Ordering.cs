namespace Fixture.Consumer;

// The StyleCop ordering tier's proof (spec §2.3 step 3). Four rules ship and only four, so this
// type is written to trip all four rather than to look like real code.
//
// The member sequence is load-bearing and fiddly for one reason: StyleCop compares each member with
// the one before it and reports only the *first* trait that is out of order — kind, then access,
// then const, then static. So a rule further down that list only fires between two members that
// already agree on everything above it. Written the obvious way, with a public static field after a
// private instance one, SA1202 fires on the access difference and SA1203 and SA1204 never get a
// look in. Each pair below therefore differs in exactly one trait. Reordering these members will
// not just move a diagnostic, it will silence one.
//
// Like Kettle next door, every diagnostic here comes from an assembly the package takes no
// dependency on — the .csproj references StyleCop.Analyzers the way a consumer does. Without that
// reference every SA line in the shipped config is inert and these assertions would pass by vacuum.
public sealed class Ordering
{
    // SA1201 — kind. A field must not follow a method, and this method leads the type.
    public int Describe() => _count + Limit + _spare + Created;

    private int _count; // expect: SA1201 warning

    // SA1203 — const. Same kind and same access as the field above, differing only in const-ness.
    private const int Limit = 4; // expect: SA1203 warning

    private int _spare;

    // SA1204 — static. Same kind, access and const-ness as the field above, differing only in
    // being static.
    private static int Created; // expect: SA1204 warning

    private void Reset()
    {
        _count = 0;
        _spare = 0;
        Created = 0;
    }

    // SA1202 — access. Same kind as the method above, differing only in being public.
    public void Clear() => Reset(); // expect: SA1202 warning
}
