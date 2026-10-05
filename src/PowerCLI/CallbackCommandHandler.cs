namespace PowerCLI;

internal sealed class CallbackCommandHandler(
    Func<CommandInvocation, CancellationToken, ValueTask<TerminalCommandResult>> callback) : ICommandHandler
{
    private readonly Func<CommandInvocation, CancellationToken, ValueTask<TerminalCommandResult>> _callback = callback;

    public ValueTask<TerminalCommandResult> ExecuteAsync(CommandInvocation invocation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _callback(invocation, cancellationToken);
    }
}
