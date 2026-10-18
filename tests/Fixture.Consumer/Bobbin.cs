namespace Fixture.Consumer;

// Triggers APB0005: a comment trailing the line it describes.
public static class Bobbin
{
    // expect: APB0005 warning
    public static int Turns => 3; // one per revolution
}
