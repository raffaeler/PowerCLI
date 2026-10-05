namespace PowerCLI;

public sealed record CommandOptionDefinition : CommandValueDefinition
{
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public string? ValueName { get; init; }
}
