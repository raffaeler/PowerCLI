namespace PowerCLI;

public interface ICommandDispatcher
{
    ValueTask<TerminalCommandResult> HandleAsync(string? input, CancellationToken cancellationToken = default);
}
