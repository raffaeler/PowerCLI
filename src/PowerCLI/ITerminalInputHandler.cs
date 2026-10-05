namespace PowerCLI;

/// <summary>Processes non-command input using behavior and state owned by the host.</summary>
public interface ITerminalInputHandler
{
    IAsyncEnumerable<TerminalOutput> HandleAsync(string input, CancellationToken cancellationToken = default);
}
