namespace Fixture.Consumer;

// Triggers APB0001: a primary constructor on a class. With the package's TreatWarningsAsErrors
// default this becomes a build error, which is exactly what the smoke test asserts.
public class Widget(int size)
{
    public int Size => size;
}

// Must stay silent: positional records are the point of a record. If APB0001's record filter
// were wrong this would also fire and the fixture would fail for the wrong reason.
public record Point(int X, int Y);

// Must stay silent: an explicit constructor is the endorsed shape.
public class Gadget
{
    private readonly int _size;

    public Gadget(int size) => _size = size;

    public int Size => _size;
}
