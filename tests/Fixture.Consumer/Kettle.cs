namespace Fixture.Consumer;

// The Meziantou tier's proof (spec §2.3 step 1). The package takes no dependency on
// Meziantou.Analyzer — the .csproj beside this file references it, exactly as a consumer would —
// so every diagnostic below is the shipped config binding to an assembly that arrived separately.
// That is the whole claim of ADR-0002 decision 1, and this file is where it is measured.
public static class Kettle
{
    // The comment below is the MA0026 (warning) violation, not a note to a future reader. It has
    // to lead with the keyword: MA0026 matches only at the start of a comment, measured. MA0026 is
    // an ordinary member of the 100-rule default-on Warning sweep, and it is here so the smoke
    // test proves the bulk of the tier rather than only the two rules given special treatment.

    // TODO deliberate MA0026 violation; see above.

    public static async Task BoilAsync()
    {
        // MA0037 (error) — an empty statement. One of the three rules Meziantou defaults to Error
        // rather than Warning; the smoke test builds once with TreatWarningsAsErrors=false to
        // prove the config records it at 'error' rather than flattening it to 'warning'.
        ;

        // Two rules meet on this one await, which is why it is written this way.
        //
        // MA0032 (warning) fires: Task.Delay has a CancellationToken overload and none is passed.
        // MA0032 is default-*off* at the vendor, so a default-on sweep would miss it; the house
        // adds it deliberately as the call-site half of the cancellation rule, completing
        // CA2016 + MA0040 + MA0032 alongside APB0002's declaration half.
        //
        // MA0004 (ConfigureAwait) must NOT fire: an await with no ConfigureAwait is precisely
        // what it reports, the assembly that implements it is loaded, and MA0032 firing on this
        // same expression proves it. MA0004 is the sole universality-test exclusion (ADR-0002
        // decision 2) and the smoke test asserts its silence here. Do not add ConfigureAwait —
        // the violating construct is the assertion.
        await Task.Delay(1);
    }
}
