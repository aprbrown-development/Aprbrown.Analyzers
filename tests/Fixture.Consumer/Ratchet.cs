namespace Fixture.Consumer;

// Triggers CA1052 in an internal type, which only `api_surface = all` reaches. CA1852 reports the
// same missing `sealed`.
// expect: CA1052 warning, CA1852 warning
internal class Ratchet
{
    public static int Teeth => 12;
}
