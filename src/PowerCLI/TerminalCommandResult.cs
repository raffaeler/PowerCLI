namespace PowerCLI;

public sealed record TerminalCommandResult(
    bool IsHandled,
    IReadOnlyList<string> Messages,
    bool ExitRequested = false,
    bool ClearScreen = false,
    IReadOnlyList<CommandDiagnostic>? Diagnostics = null,
    IAsyncEnumerable<TerminalOutput>? Output = null)
{
    public static TerminalCommandResult NotACommand { get; } = new(false, []);
    public static TerminalCommandResult Message(params string[] messages) => new(true, messages);
}
