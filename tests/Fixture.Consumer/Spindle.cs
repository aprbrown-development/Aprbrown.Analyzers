namespace Fixture.Consumer;

// Triggers APB0004: the expression body's arrow starts the line below the declaration instead of
// ending the declaration line. The marker sits on the arrow's line, where the diagnostic is reported.
public class Spindle
{
    public int Speed
        => 1200; // expect: APB0004 warning
}
