// System.Threading and System.Threading.Tasks arrive via ImplicitUsings.
namespace Fixture.Consumer;

// Triggers APB0002: a defaulted CancellationToken parameter. With the package's
// TreatWarningsAsErrors default this becomes a build error, which the smoke test asserts.
public static class Sprocket
{
    public static Task SpinAsync(CancellationToken ct = default) => Task.CompletedTask;

    // Must stay silent: a token the caller is obliged to pass is the endorsed shape.
    public static Task WindAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
