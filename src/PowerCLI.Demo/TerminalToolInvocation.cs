namespace PowerCLI.Demo;

/// <summary>Arguments supplied to a demo tool invocation.</summary>
public sealed record TerminalToolInvocation(IReadOnlyDictionary<string, string> Arguments);
