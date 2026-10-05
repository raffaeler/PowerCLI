namespace PowerCLI;

/// <summary>Configures a named option and generates its required/repeated usage syntax.</summary>
public sealed class CommandOptionBuilder : CommandValueBuilder<CommandOptionBuilder>
{
    private readonly string? _valueName;
    private readonly List<string> _aliases = [];
    private bool _required;
    private bool _repeated;

    internal CommandOptionBuilder(string location, string? valueName) : base(location)
    {
        _valueName = valueName;
        if (valueName is null) Type("boolean");
    }

    protected override CommandOptionBuilder Self => this;

    public CommandOptionBuilder Alias(params string[] aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        _aliases.AddRange(aliases);
        return this;
    }

    public CommandOptionBuilder Required()
    {
        _required = true;
        return this;
    }

    public CommandOptionBuilder Repeat()
    {
        _repeated = true;
        return this;
    }

    internal string Syntax(string name)
    {
        var syntax = _valueName is null ? name : $"{name} <{_valueName}>";
        return _repeated ? (_required ? $"({syntax})+" : $"[{syntax}]*") :
            _required ? syntax : $"[{syntax}]";
    }

    internal CommandOptionDefinition BuildOption(string prefix, Dictionary<string, ICommandChoiceProvider> providers,
        Dictionary<string, ICommandValidator> validators)
    {
        var definition = Build(prefix, providers, validators);
        return new()
        {
            ValueName = _valueName,
            Aliases = _aliases.ToArray(),
            Type = definition.Type,
            Description = definition.Description,
            Default = definition.Default,
            Min = definition.Min,
            Max = definition.Max,
            MinLength = definition.MinLength,
            MaxLength = definition.MaxLength,
            Choices = definition.Choices,
            Validator = definition.Validator
        };
    }
}
