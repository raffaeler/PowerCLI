namespace PowerCLI;

/// <summary>Declares positional syntax, value metadata, named options, and a callback.</summary>
public sealed class CommandFormBuilder
{
    private readonly string _command;
    private readonly string _syntax;
    private readonly Dictionary<string, CommandArgumentBuilder> _arguments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CommandOptionBuilder> _options = new(StringComparer.Ordinal);
    private readonly List<string> _examples = [];
    private Func<CommandInvocation, CancellationToken, ValueTask<TerminalCommandResult>>? _handler;

    internal CommandFormBuilder(string command, string syntax)
    {
        _command = command;
        _syntax = syntax;
    }

    public CommandFormBuilder Argument(string name, Action<CommandArgumentBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        var argument = new CommandArgumentBuilder($"command '{_command}', argument '{name}'");
        configure?.Invoke(argument);
        if (!_arguments.TryAdd(name, argument))
            throw new CommandConfigurationException($"command '{_command}': duplicate argument '{name}'.");
        return this;
    }

    public CommandFormBuilder Option(string name, string valueName, Action<CommandOptionBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(valueName);
        return AddOption(name, valueName, configure);
    }

    public CommandFormBuilder Flag(string name, Action<CommandOptionBuilder>? configure = null) =>
        AddOption(name, null, configure);

    public CommandFormBuilder Example(params string[] examples)
    {
        ArgumentNullException.ThrowIfNull(examples);
        _examples.AddRange(examples);
        return this;
    }

    public CommandFormBuilder Handle(Func<CommandInvocation, TerminalCommandResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Handle((invocation, _) => ValueTask.FromResult(handler(invocation)));
    }

    public CommandFormBuilder Handle(Func<CommandInvocation, CancellationToken, ValueTask<TerminalCommandResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (_handler is not null)
            throw new CommandConfigurationException($"command '{_command}', syntax '{_syntax}': handler already declared.");
        _handler = handler;
        return this;
    }

    internal CommandFormDefinition Build(string prefix, Dictionary<string, ICommandHandler> handlers,
        Dictionary<string, ICommandChoiceProvider> providers, Dictionary<string, ICommandValidator> validators)
    {
        if (_handler is null)
            throw new CommandConfigurationException($"command '{_command}', syntax '{_syntax}': a handler is required.");
        handlers.Add(prefix, new CallbackCommandHandler(_handler));
        return new()
        {
            Id = prefix,
            Handler = prefix,
            Syntax = string.Join(' ', new[] { _syntax }.Concat(_options.Select(option => option.Value.Syntax(option.Key)))
                .Where(part => part.Length > 0)),
            Arguments = _arguments.Select((argument, index) => new KeyValuePair<string, CommandValueDefinition>(
                argument.Key, argument.Value.Build($"{prefix}.argument{index}", providers, validators)))
                .ToDictionary(StringComparer.Ordinal),
            Options = _options.Select((option, index) => new KeyValuePair<string, CommandOptionDefinition>(
                option.Key, option.Value.BuildOption($"{prefix}.option{index}", providers, validators)))
                .ToDictionary(StringComparer.Ordinal),
            Examples = _examples.ToArray()
        };
    }

    private CommandFormBuilder AddOption(string name, string? valueName, Action<CommandOptionBuilder>? configure)
    {
        ArgumentNullException.ThrowIfNull(name);
        var option = new CommandOptionBuilder($"command '{_command}', option '{name}'", valueName);
        configure?.Invoke(option);
        if (!_options.TryAdd(name, option))
            throw new CommandConfigurationException($"command '{_command}': duplicate option '{name}'.");
        return this;
    }
}
