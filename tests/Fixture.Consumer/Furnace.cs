namespace Fixture.Consumer;

// The SDK CA tier's proof (spec §2.3 step 2), and the seal's.
//
// Everything in this file is measured against one control: CA2200 fires here, so the SDK's
// analyzers are loaded and analysing this exact file. That is what makes the two silences below
// evidence of the ruleset rather than evidence of an absent analyzer — the same argument Kettle
// makes for MA0004.
public static class Furnace
{
    // CA2200 (warning) — rethrowing the caught variable discards the stack. An ordinary member of
    // the 27-rule Warning sweep, and the control for the two silences further down.
    public static void Relight()
    {
        try
        {
            throw new InvalidOperationException("deliberate CA2200 violation; see above");
        }
        catch (InvalidOperationException e)
        {
            throw e;
        }
    }

    // CA2252 (error) — calling an API marked [RequiresPreviewFeatures] without opting in. One of
    // the two rules the SDK defaults to Error rather than Warning; the smoke test builds once with
    // TreatWarningsAsErrors=false to prove the config records it at 'error' rather than flattening
    // it to 'warning', exactly as it does for Meziantou's MA0037.
    //
    // These two are also why this tier ships 29 rules where spec §2.3 step 2 says 27: the 27 is the
    // Warning subset, and dropping the vendor's Error rules to reach it would flatten a severity
    // the spec says to read rather than assume.
    public static void Prime() => Prototype.Ignite();

    // CA2007 must NOT fire. An await written without ConfigureAwait is precisely what it reports,
    // and the SDK analyzer that implements it is loaded — CA2200 above proves that. It is
    // enabled-by-default *false* upstream and stays off under the blanket, which is the half of
    // ADR-0002 decision 4 about default-off rules never being admitted. Do not add ConfigureAwait:
    // the violating construct is the assertion.
    public static async Task WarmAsync() => await Task.CompletedTask;

    // CA1707 must NOT fire. The house convention is _camelCase private fields, so a rule banning
    // underscores in identifiers is one this ruleset specifically wants off — and it is the direct
    // regression test for re-enabling by category, which is the cheapest way to break the seal and
    // would pass every other assertion in the smoke test.
    //
    // Worth being exact about how it stays off, because ADR-0002 decision 4 calls it default-off
    // and it is not: measured against SDK 10.0.110 it is enabled by default at *Hidden*. Hidden
    // does not gate a build, so this silence is weaker evidence than that passage assumed — a
    // Naming category re-enable would set it to warning and it would fire here. The load-bearing
    // guards are CA2007 above, which is genuinely default-off at Warning, and the smoke test's
    // structural check that no category line exists in the packed config at all.
    public static void Set_Point()
    {
    }
}
