namespace PowerCLI.Tests;

internal sealed class FakeTimeProvider : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 10, 5, 7, 25, 37, TimeSpan.Zero);
}
