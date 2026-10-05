namespace PowerCLI.Demo;

/// <summary>Approves only the built-in UTC tool without user interaction.</summary>
public sealed class BuiltInToolApprovalPolicy : IToolApprovalPolicy
{
    public ValueTask<bool> ApproveAsync(TerminalTool tool, TerminalToolInvocation invocation, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(string.Equals(tool.Id, "get_current_utc_time", StringComparison.Ordinal));
}
