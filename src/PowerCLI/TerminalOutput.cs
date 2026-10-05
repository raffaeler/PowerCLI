namespace PowerCLI;

/// <summary>A plain line or an incremental Markdown fragment supplied by a host.</summary>
public sealed record TerminalOutput(
    string Content,
    TerminalOutputKind Kind = TerminalOutputKind.Text,
    string? Prefix = null,
    TerminalTextStyle Style = TerminalTextStyle.Plain);
