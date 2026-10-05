namespace PowerCLI.Demo;

public interface ITerminalWorkflowRunner
{
    ValueTask<IReadOnlyList<TerminalActivity>> RunAsync(string workflowId, string prompt, TerminalSelection selection, CancellationToken cancellationToken = default);
}
