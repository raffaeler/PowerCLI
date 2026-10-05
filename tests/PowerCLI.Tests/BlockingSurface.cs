namespace PowerCLI.Tests;

internal sealed class BlockingSurface(IEnumerable<ConsoleKeyInfo>? keys = null) : FakeSurface(keys ?? [])
{
    public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken = default)
    {
        if (RemainingKeys > 0) return await base.ReadKeyAsync(cancellationToken);
        ReadStarted.TrySetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException("Unreachable.");
    }
}
