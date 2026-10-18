namespace Fixture.Consumer;

// CA1849 reports a blocking call in an async method when an awaitable overload exists; the awaited
// call below it is the near-miss that must stay silent.
public static class Pump
{
    public static async Task PrimeAsync(Stream stream)
    {
        // expect: CA1849 warning
        stream.Flush();
        await stream.FlushAsync(CancellationToken.None);
    }
}
