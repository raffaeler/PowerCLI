namespace PowerCLI;

/// <summary>Declares a command's descriptions, aliases, and literal-distinguished forms.</summary>
public sealed class CommandBuilder
{
    private readonly string _name;
    private readonly List<string> _aliases = [];
    private readonly List<CommandFormBuilder> _forms = [];
    private string _description = "";

    internal CommandBuilder(string name) => _name = name;

    public CommandBuilder Description(string description)
    {
        ArgumentNullException.ThrowIfNull(description);
        _description = description;
        return this;
    }

    public CommandBuilder Alias(params string[] aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        _aliases.AddRange(aliases);
        return this;
    }

    public CommandBuilder Form(string syntax, Action<CommandFormBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(syntax);
        ArgumentNullException.ThrowIfNull(configure);
        var form = new CommandFormBuilder(_name, syntax);
        configure(form);
        _forms.Add(form);
        return this;
    }

    internal CommandDefinition Build(string prefix, Dictionary<string, ICommandHandler> handlers,
        Dictionary<string, ICommandChoiceProvider> providers, Dictionary<string, ICommandValidator> validators) =>
        new()
        {
            Name = _name,
            Description = _description,
            Aliases = _aliases.ToArray(),
            Forms = _forms.Select((form, index) =>
                form.Build($"{prefix}.form{index}", handlers, providers, validators)).ToArray()
        };
}
