namespace PowerCLI.Demo;

public interface ITerminalAgent
{
    IAsyncEnumerable<TerminalActivity> RespondAsync(string prompt, TerminalSelection selection, CancellationToken cancellationToken = default);
}
