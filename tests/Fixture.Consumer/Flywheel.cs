namespace Fixture.Consumer;

// Triggers IDE0004, IDE0051 and IDE0052. Inertia is the near-miss: a private member that is used.
public sealed class Flywheel
{
    // expect: IDE0052 warning
    private int _lastSpeed;

    public void Spin(int speed) => _lastSpeed = speed;

    // expect: IDE0004 warning
    public int Mass() => (int)Inertia();

    private int Inertia() => 4;

    // expect: IDE0051 warning
    private void Brake()
    {
    }
}
