using PowerCLI.Demo;

namespace PowerCLI.Tests;

internal sealed class RecordingWorkflowRunner : ITerminalWorkflowRunner
{
    internal List<string> Workflows { get; } = [];
    public ValueTask<IReadOnlyList<TerminalActivity>> RunAsync(string workflowId, string prompt, TerminalSelection selection, CancellationToken cancellationToken = default)
    {
        Workflows.Add(workflowId);
        return ValueTask.FromResult<IReadOnlyList<TerminalActivity>>([new(TerminalActivityKind.Status, "workflow complete")]);
    }
}
