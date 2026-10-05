using PowerCLI;

namespace PowerCLI.Demo;

internal sealed class InteractiveApprovalPolicy(ITerminalConsole console) : IToolApprovalPolicy
{
    private readonly ITerminalConsole _console = console;
    public async ValueTask<bool> ApproveAsync(TerminalTool tool, TerminalToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        if (_console.IsInputRedirected || _console.IsOutputRedirected) return false;
        var editor = new InteractiveLineEditor(_console);
        var picked = await editor.PickAsync($"Approve '{tool.Id}'?", [new("no", "Deny"), new("yes", "Approve")], cancellationToken: cancellationToken);
        return picked.Any(option => option.Value == "yes");
    }
}
