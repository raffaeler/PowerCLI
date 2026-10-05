namespace PowerCLI.Demo;

/// <summary>A demo tool made available to an agent or workflow.</summary>
public sealed record TerminalTool(
    string Id,
    string Description,
    bool IsTrusted,
    Func<TerminalToolInvocation, CancellationToken, ValueTask<string>> InvokeAsync);
