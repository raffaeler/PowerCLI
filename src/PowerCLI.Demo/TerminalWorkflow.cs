namespace PowerCLI.Demo;

/// <summary>A workflow that can be inspected or invoked from the demo.</summary>
public sealed record TerminalWorkflow(
    string Id,
    string DisplayName,
    string Description,
    string Diagram,
    bool IsSubworkflow = false,
    bool AcceptsPrompt = true,
    bool IsTrusted = true);
