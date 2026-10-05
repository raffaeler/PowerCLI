namespace PowerCLI;

public sealed record CommandDefinition
{
    public required string Name { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public string Description { get; init; } = "";
    public IReadOnlyList<CommandFormDefinition> Forms { get; init; } = [];
}
