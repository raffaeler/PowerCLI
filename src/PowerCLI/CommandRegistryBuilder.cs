namespace PowerCLI;

/// <summary>Registers commands and callbacks using the existing command configuration engine.</summary>
public sealed class CommandRegistryBuilder
{
    private readonly List<CommandBuilder> _commands = [];

    public CommandRegistryBuilder Command(string name, Action<CommandBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);
        var command = new CommandBuilder(name);
        configure(command);
        _commands.Add(command);
        return this;
    }

    public CommandRegistry Build()
    {
        var handlers = new Dictionary<string, ICommandHandler>();
        var providers = new Dictionary<string, ICommandChoiceProvider>();
        var validators = new Dictionary<string, ICommandValidator>();
        var configuration = new CommandConfiguration
        {
            Commands = _commands.Select((command, index) =>
                command.Build($"fluent.command{index}", handlers, providers, validators)).ToArray()
        };
        return new(configuration, handlers, providers, validators);
    }
}
