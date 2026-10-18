namespace Fixture.Consumer;

// Triggers CA1725 in an internal type, which only `api_surface = all` reaches.
internal sealed class Idler : Gear
{
    // expect: CA1725 warning
    public override void Turn(int angle)
    {
    }
}
