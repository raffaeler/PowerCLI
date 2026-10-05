namespace PowerCLI.Demo;

/// <summary>A catalog document that can be displayed or selected by the demo.</summary>
public sealed record TerminalDocument(
    string Id,
    string DisplayName,
    TerminalDocumentKind Kind,
    string Body,
    bool IsTrusted = true,
    bool IsEnabled = true,
    string? Description = null);
