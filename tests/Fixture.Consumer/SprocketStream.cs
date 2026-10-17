namespace Fixture.Consumer;

// Must stay silent, and carries no expectation marker so the smoke test asserts it. The override
// keeps the default Stream.ReadAsync declares: APB0002 exempts it, and MA0061 would fire on the
// same line were the default dropped, so this is the one shape where both rules must agree.
public abstract class SprocketStream : Stream
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(0);
}
