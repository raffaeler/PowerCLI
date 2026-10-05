namespace PowerCLI;

public record CommandValueDefinition
{
    public string Type { get; init; } = "string";
    public string Description { get; init; } = "";
    public string? Default { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }
    public int? MinLength { get; init; }
    public int? MaxLength { get; init; }
    public CommandChoices? Choices { get; init; }
    public string? Validator { get; init; }
}
