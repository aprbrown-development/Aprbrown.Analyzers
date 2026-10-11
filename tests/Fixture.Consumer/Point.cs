namespace Fixture.Consumer;

// Must stay silent: positional records are the point of a record. It carries no expectation
// marker, so if APB0001's record filter were wrong the diagnostic here would fail the smoke test as
// unexpected.
public record Point(int X, int Y);
