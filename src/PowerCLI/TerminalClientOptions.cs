namespace PowerCLI;

/// <summary>Host-configurable text used by the terminal loop.</summary>
public sealed record TerminalClientOptions
{
    public string Prompt { get; init; } = "> ";
    public string? WelcomeMessage { get; init; } = "Type /help for commands.";
}
