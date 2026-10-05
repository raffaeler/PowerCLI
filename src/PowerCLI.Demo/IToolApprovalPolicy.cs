namespace PowerCLI.Demo;

/// <summary>Controls whether a requested tool may be invoked.</summary>
public interface IToolApprovalPolicy
{
    ValueTask<bool> ApproveAsync(TerminalTool tool, TerminalToolInvocation invocation, CancellationToken cancellationToken = default);
}
