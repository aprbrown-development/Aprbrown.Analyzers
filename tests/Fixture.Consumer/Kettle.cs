namespace Fixture.Consumer;

// The Meziantou tier's proof (spec §2.3 step 1). The package takes no dependency on
// Meziantou.Analyzer — the .csproj beside this file references it, exactly as a consumer would —
// so every diagnostic below is the shipped config binding to an assembly that arrived separately.
// That is the whole claim of ADR-0002 decision 1, and this file is where it is measured.
//
// The three rules declared here are a deliberate sample of the 106, not all of them. What the other
// 103 get is weaker and worth naming: the expected ruleset pins that they are present at the right
// severities, but nothing observes them fire. Proving every one behaviourally would mean 103 more
// violations here, each re-demonstrating the binding these three already establish.
public static class Kettle
{
    // The comment below is the MA0026 (warning) violation, not a note to a future reader. It has
    // to lead with the keyword: MA0026 matches only at the start of a comment, measured. MA0026 is
    // an ordinary member of the 103-rule default-on Warning sweep, and it is here so the smoke
    // test proves the bulk of the tier rather than only the two rules given special treatment.

    // expect: MA0026 warning
    // TODO deliberate MA0026 violation; see above.

    public static async Task BoilAsync()
    {
        // MA0037 (error) — an empty statement. One of the three rules Meziantou defaults to Error
        // rather than Warning; the smoke test builds once with TreatWarningsAsErrors=false to
        // prove the config records it at 'error' rather than flattening it to 'warning'.
        // expect: MA0037 error
        ;

        // Two rules meet on this one await, which is why it is written this way.
        //
        // MA0032 (warning) fires: Task.Delay has a CancellationToken overload and none is passed.
        // MA0032 is default-*off* at the vendor, so a default-on sweep would miss it; the house
        // adds it deliberately as the call-site half of the cancellation rule, opposite APB0002's
        // declaration half. CA2016 and MA0040 default to Info and stay off (spec §10).
        //
        // MA0004 (ConfigureAwait) must NOT fire: an await with no ConfigureAwait is precisely
        // what it reports, the assembly that implements it is loaded, and MA0032 firing on this
        // same expression proves it. MA0004 is the sole universality-test exclusion (ADR-0002
        // decision 2) and the smoke test asserts its silence here. Do not add ConfigureAwait —
        // the violating construct is the assertion.
        // expect: MA0032 warning
        await Task.Delay(1);
    }
}
