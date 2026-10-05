namespace PowerCLI.Demo;

public sealed record TerminalActivity(TerminalActivityKind Kind, string Content, string? ToolId = null,
    IReadOnlyDictionary<string, string>? ToolArguments = null);
