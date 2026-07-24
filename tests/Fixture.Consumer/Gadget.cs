namespace Fixture.Consumer;

// Must stay silent: an explicit constructor is the endorsed shape.
public class Gadget
{
    private readonly int _size;

    public Gadget(int size) => _size = size;

    public int Size => _size;
}
