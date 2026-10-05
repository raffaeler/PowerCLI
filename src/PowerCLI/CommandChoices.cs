namespace PowerCLI;

public sealed record CommandChoices
{
    public IReadOnlyList<string>? Values { get; init; }
    public string? Provider { get; init; }
    public string Validation { get; init; } = "closed";
    public string Selection { get; init; } = "single";
    public bool CaseSensitive { get; init; }
}
