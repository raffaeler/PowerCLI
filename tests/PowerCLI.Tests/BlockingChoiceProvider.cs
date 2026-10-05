using PowerCLI;

namespace PowerCLI.Tests;

internal sealed class BlockingChoiceProvider : ICommandChoiceProvider
{
    internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async ValueTask<CommandChoiceSnapshot> GetChoicesAsync(CommandChoiceContext context, CancellationToken cancellationToken = default)
    {
        Started.TrySetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException("Unreachable.");
    }
}
