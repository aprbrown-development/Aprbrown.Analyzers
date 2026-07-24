namespace Fixture.Consumer;

// Triggers APB0001: a primary constructor on a class. With the package's TreatWarningsAsErrors
// default this becomes a build error, which is exactly what the smoke test asserts.
public class Widget(int size)
{
    public int Size => size;
}
