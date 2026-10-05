namespace PowerCLI;

internal sealed class CallbackCommandChoiceProvider(
    Func<CommandChoiceContext, CancellationToken, ValueTask<CommandChoiceSnapshot>> callback) : ICommandChoiceProvider
{
    private readonly Func<CommandChoiceContext, CancellationToken, ValueTask<CommandChoiceSnapshot>> _callback = callback;

    public ValueTask<CommandChoiceSnapshot> GetChoicesAsync(CommandChoiceContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _callback(context, cancellationToken);
    }
}
