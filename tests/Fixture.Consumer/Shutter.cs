namespace Fixture.Consumer;

// Triggers APB0003 and nothing else: an explicit implementation renames IResizable's parameter, which
// CA1725 also reports until APBS0001 suppresses it.
public class Shutter : IResizable
{
    // expect: APB0003 warning
    void IResizable.Resize(int size)
    {
    }
}
