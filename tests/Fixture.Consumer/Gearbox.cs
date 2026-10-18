namespace Fixture.Consumer;

// Triggers CA1725: a public override renames its base's parameter.
public class Gearbox : Gear
{
    // expect: CA1725 warning
    public override void Turn(int angle)
    {
    }
}
