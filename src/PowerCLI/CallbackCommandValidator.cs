namespace PowerCLI;

internal sealed class CallbackCommandValidator(
    Func<CommandInvocation, string, CommandValue, CancellationToken, ValueTask<string?>> callback) : ICommandValidator
{
    private readonly Func<CommandInvocation, string, CommandValue, CancellationToken, ValueTask<string?>> _callback = callback;

    public ValueTask<string?> ValidateAsync(CommandInvocation invocation, string name, CommandValue value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _callback(invocation, name, value, cancellationToken);
    }
}
