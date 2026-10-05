using PowerCLI.Demo;

namespace PowerCLI.Tests;

internal sealed class RecordingApprovalPolicy : IToolApprovalPolicy
{
    internal int Calls { get; private set; }
    public ValueTask<bool> ApproveAsync(TerminalTool tool, TerminalToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        Calls++;
        return ValueTask.FromResult(true);
    }
}
