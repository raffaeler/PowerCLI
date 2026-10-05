namespace PowerCLI.Tests;

internal sealed class BlockingSurface() : FakeSurface([])
{
    public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken = default)
    {
        ReadStarted.SetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException("Unreachable.");
    }
}
