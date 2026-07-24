namespace Fixture.Consumer;

// Must stay silent: positional records are the point of a record. If APB0001's record filter
// were wrong this would also fire and the fixture would fail for the wrong reason.
public record Point(int X, int Y);
