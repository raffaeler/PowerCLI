namespace PowerCLI;

public sealed record CommandFormDefinition
{
    public required string Id { get; init; }
    public required string Syntax { get; init; }
    public required string Handler { get; init; }
    public IReadOnlyDictionary<string, CommandValueDefinition> Arguments { get; init; } = new Dictionary<string, CommandValueDefinition>();
    public IReadOnlyDictionary<string, CommandOptionDefinition> Options { get; init; } = new Dictionary<string, CommandOptionDefinition>();
    public IReadOnlyList<string> Examples { get; init; } = [];
}
