// System.Threading and System.Threading.Tasks arrive via ImplicitUsings.
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

// Triggers APB0002: a defaulted CancellationToken parameter. Same deal as Widget — the package's
// TreatWarningsAsErrors default turns this into a build error, which the smoke test asserts.
public static class Sprocket
{
    public static Task SpinAsync(CancellationToken ct = default) => Task.CompletedTask;

    // Must stay silent: a token the caller is obliged to pass is the endorsed shape.
    public static Task WindAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
