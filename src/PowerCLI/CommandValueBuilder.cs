namespace PowerCLI;

/// <summary>Configures existing scalar metadata and optional choice/validation callbacks.</summary>
public abstract class CommandValueBuilder<TBuilder> where TBuilder : CommandValueBuilder<TBuilder>
{
    private readonly string _location;
    private CommandValueDefinition _definition = new();
    private Func<CommandChoiceContext, CancellationToken, ValueTask<CommandChoiceSnapshot>>? _provider;
    private Func<CommandInvocation, string, CommandValue, CancellationToken, ValueTask<string?>>? _validator;
    private bool _multiple;
    private bool _suggestions;
    private bool _caseSensitive;

    private protected CommandValueBuilder(string location) => _location = location;
    protected abstract TBuilder Self { get; }

    public TBuilder Type(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        _definition = _definition with { Type = type };
        return Self;
    }

    public TBuilder Description(string description)
    {
        ArgumentNullException.ThrowIfNull(description);
        _definition = _definition with { Description = description };
        return Self;
    }

    public TBuilder Default(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _definition = _definition with { Default = value };
        return Self;
    }

    public TBuilder Bounds(double? min = null, double? max = null)
    {
        _definition = _definition with { Min = min, Max = max };
        return Self;
    }

    public TBuilder Length(int? min = null, int? max = null)
    {
        _definition = _definition with { MinLength = min, MaxLength = max };
        return Self;
    }

    public TBuilder Choices(params string[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        CheckChoices();
        _definition = _definition with { Choices = new() { Values = values.ToArray() } };
        return Self;
    }

    public TBuilder Choices(Func<CommandChoiceContext, CancellationToken, ValueTask<CommandChoiceSnapshot>> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        CheckChoices();
        _provider = provider;
        _definition = _definition with { Choices = new() { Provider = "callback" } };
        return Self;
    }

    public TBuilder Multiple()
    {
        _multiple = true;
        return Self;
    }

    public TBuilder Suggestions()
    {
        _suggestions = true;
        return Self;
    }

    public TBuilder CaseSensitive()
    {
        _caseSensitive = true;
        return Self;
    }

    public TBuilder Validate(Func<CommandInvocation, string, CommandValue, CancellationToken, ValueTask<string?>> validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        if (_validator is not null)
            throw new CommandConfigurationException($"{_location}: validator already declared.");
        _validator = validator;
        return Self;
    }

    internal CommandValueDefinition Build(string prefix, Dictionary<string, ICommandChoiceProvider> providers,
        Dictionary<string, ICommandValidator> validators)
    {
        if (_definition.Choices is null && (_multiple || _suggestions || _caseSensitive))
            throw new CommandConfigurationException($"{_location}: choice settings require choices.");
        var definition = _definition;
        if (definition.Choices is { } choices)
        {
            if (_provider is not null)
            {
                providers.Add(prefix, new CallbackCommandChoiceProvider(_provider));
                choices = choices with { Provider = prefix };
            }
            definition = definition with { Choices = choices with
            {
                Selection = _multiple ? "multiple" : "single",
                Validation = _suggestions ? "suggestions" : "closed",
                CaseSensitive = _caseSensitive
            } };
        }
        if (_validator is not null)
        {
            validators.Add(prefix, new CallbackCommandValidator(_validator));
            definition = definition with { Validator = prefix };
        }
        return definition;
    }

    private void CheckChoices()
    {
        if (_definition.Choices is not null)
            throw new CommandConfigurationException($"{_location}: choices already declared.");
    }
}
