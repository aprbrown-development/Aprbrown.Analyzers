namespace Fixture.Consumer;

// The base declaration for the CA1725 cases; Gearbox and Idler override it and rename the parameter.
public abstract class Gear
{
    public abstract void Turn(int degrees);
}
