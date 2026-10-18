namespace Fixture.Consumer;

// Triggers APB0006: a conditional expression nested in another's false branch. The marker declares
// the inner conditional's line, where the diagnostic is reported.
public static class Governor
{
    public static string Band(int rpm) =>
        rpm < 1000
            ? "idle"
            // expect: APB0006 warning
            : rpm < 3000 ? "cruise" : "redline";
}
