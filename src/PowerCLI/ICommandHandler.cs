namespace PowerCLI;

public interface ICommandHandler
{
    ValueTask<TerminalCommandResult> ExecuteAsync(CommandInvocation invocation, CancellationToken cancellationToken = default);
}
