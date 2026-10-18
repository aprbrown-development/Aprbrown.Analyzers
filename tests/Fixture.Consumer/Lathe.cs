namespace Fixture.Consumer;

// Proves the IDE, naming and style tier reaches a consumer.
//
// This tier is the one where "enumerated by ID" is not a stylistic preference but the whole
// mechanism: under the blanket, a naming rule whose dotnet_diagnostic severity is not set
// explicitly is *silently unenforced* — it neither fires nor warns that it is inactive. So
// unlike the CA tier next door there is no vendor default to fall back on, and nothing here would
// fire by accident. Every diagnostic below exists because the shipped config names its ID.
//
// Unlike Kettle and Ordering, this file needs no package reference at all: IDE rules are the
// compiler's own, reached at build time through the EnforceCodeStyleInBuild default the package
// ships. That property is load-bearing for this file the way the Meziantou reference is for
// Kettle — without it every line below is inert.
//
// Field order is static-before-instance so SA1204 stays out of the way; the four SA rules are
// proven next door in Ordering.cs and have no business firing here.
public sealed class Lathe
{
    // IDE1006, and the more interesting of the two naming cases. `_created` satisfies the
    // *general* private-fields rule and violates the *specific* private-statics one, so it fires
    // only if Roslyn picks the more specific rule. It does: naming rules are ordered by
    // specificity, not by declaration order. This field sits *before* the general rule's own
    // violation below, so were the rules ordered by declaration the general rule would claim it
    // and this marker would go unmet.
    // expect: IDE1006 warning
    private static int _created = 1;

    // Must stay silent: a private static in PascalCase is what the specific rule asks for.
    private static int Produced = 2;

    // IDE1006 — a private instance field with no `_` prefix, against
    // dotnet_naming_rule.private_fields_are_underscored. IDE1006 is the single ID every
    // dotnet_naming_rule violation reports under, which is why the shipped config must enumerate
    // it for any of the naming block to do anything at all.
    // expect: IDE1006 warning
    private int count = 3;

    // Must stay silent: the shape the general rule asks for, so the file proves the rules fire on
    // the right thing rather than merely firing.
    private readonly int _size;

    public Lathe(int size) => _size = size;

    // IDE0040 again, on an interface member, which is the half `always` buys over
    // `for_non_interface_members`. C# infers `public` here and the house rule still wants it
    // written. Nested so MA0048's file-per-type rule has nothing to say about it, and placed here
    // rather than at the end of the type because StyleCop's element order puts nested interfaces
    // *before* methods, not after — SA1201 measured, not guessed.
    public interface ILatheStep
    {
        // expect: IDE0040 warning
        void Tighten();
    }

    // IDE0022 — a block body wrapping a single expression. One of the seven expression-bodied
    // preferences, which ship at `warning` and gate like any other rule. The marker declares the
    // declaration line, where IDE0022 reports, so the two-build diff proves the raised severity survives
    // packing — an error under TreatWarningsAsErrors, a warning without it.
    // expect: IDE0022 warning
    public int Describe()
    {
        return count + _size + _created + Produced;
    }

    // IDE0003 must NOT fire, and that is a limitation rather than a decision. `this.` on a field
    // is exactly what it reports, `dotnet_style_qualification_for_field = false` is what tells it
    // so, and both are in place — but IDE0003 is one of the Simplify-family rules Roslyn runs only
    // in the IDE, so a command-line build never reports it. Measured on SDK 10.0.110: IDE0009, the
    // add-qualification half, fires from the same options in the same build; IDE0003 does not.
    //
    // The pair still ships. In an editor both halves work, which is where a developer meets them —
    // and this is the one house convention the build does not gate. The smoke test pins the
    // silence so an SDK that fixes it cannot do so unnoticed.
    public int Measure() => this._size;

    // IDE0040 — no accessibility modifier, against
    // dotnet_style_require_accessibility_modifiers = always. C# would infer `private`; the house
    // rule is that access is always something the author chose rather than something the reader
    // has to know the language rules to recover. Never called, so IDE0051 reports it too.
    // expect: IDE0040 warning, IDE0051 warning
    void Idle()
    {
    }
}
