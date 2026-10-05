using PowerCLI;

namespace PowerCLI.Demo;

internal sealed class BogusWorkflowRunner : ITerminalWorkflowRunner
{
    public ValueTask<IReadOnlyList<TerminalActivity>> RunAsync(string workflowId, string prompt, TerminalSelection selection, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<TerminalActivity>>(
        [
            new(TerminalActivityKind.Status, $"Workflow '{workflowId}' started."),
            new(TerminalActivityKind.Answer, $"The bogus workflow processed: **{prompt}**."),
            new(TerminalActivityKind.Status, "Workflow completed.")
        ]);
}
