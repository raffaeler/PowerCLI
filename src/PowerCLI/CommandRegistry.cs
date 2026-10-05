using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerCLI;

public sealed class CommandRegistry
{
    private readonly FrozenDictionary<string, CompiledCommand> _names;
    private readonly IReadOnlyList<CompiledCommand> _commands;
    private readonly FrozenDictionary<string, ICommandHandler> _handlers;
    private readonly FrozenDictionary<string, ICommandChoiceProvider> _providers;
    private readonly FrozenDictionary<string, ICommandValidator> _validators;
    private readonly CompiledCommand _help;

    public CommandRegistry(CommandConfiguration configuration,
        IReadOnlyDictionary<string, ICommandHandler>? handlers = null,
        IReadOnlyDictionary<string, ICommandChoiceProvider>? providers = null,
        IReadOnlyDictionary<string, ICommandValidator>? validators = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.SchemaVersion != 1) throw new CommandConfigurationException("$.schemaVersion: only version 1 is supported.");
        ValidateCollections(configuration);
        _handlers = (handlers ?? new Dictionary<string, ICommandHandler>()).ToFrozenDictionary(StringComparer.Ordinal);
        _providers = (providers ?? new Dictionary<string, ICommandChoiceProvider>()).ToFrozenDictionary(StringComparer.Ordinal);
        _validators = (validators ?? new Dictionary<string, ICommandValidator>()).ToFrozenDictionary(StringComparer.Ordinal);
        if (_handlers.Any(pair => pair.Value is null) || _providers.Any(pair => pair.Value is null) || _validators.Any(pair => pair.Value is null))
            Fail("Host registration entries cannot be null.");
        // Snapshot the host's mutable collections. The compiled snapshot is never exposed.
        configuration = CommandConfiguration.FromJson(JsonSerializer.Serialize(configuration, CommandConfiguration.JsonOptions));
        var names = new Dictionary<string, CompiledCommand>(StringComparer.OrdinalIgnoreCase);
        var commands = new List<CompiledCommand>();
        foreach (var (definition, commandIndex) in configuration.Commands.Select((definition, index) => (definition, index)))
        {
            string? formLocation = null;
            try
            {
                if (definition.Forms.Count == 0) Fail("At least one form is required.");
                var ids = new HashSet<string>(StringComparer.Ordinal);
                var forms = new List<CompiledForm>();
                foreach (var (form, formIndex) in definition.Forms.Select((form, index) => (form, index)))
                {
                    formLocation = $"$.commands[{commandIndex}].forms[{formIndex}], command '{definition.Name}', form '{form.Id}'";
                    if (string.IsNullOrWhiteSpace(form.Id) || !ids.Add(form.Id)) Fail("Form IDs must be nonempty and unique.");
                    if (string.IsNullOrWhiteSpace(form.Handler) || !_handlers.ContainsKey(form.Handler)) Fail($"form '{form.Id}': handler '{form.Handler}' is not registered.");
                    var compiled = new CommandSyntaxCompiler(form).Compile();
                    foreach (var argument in form.Arguments)
                    {
                        var occurrences = compiled.Branches.SelectMany(branch => branch).Where(part => part.IsCapture && part.Name == argument.Key).ToArray();
                        ValidateMetadata(argument.Key, argument.Value, occurrences.Any(part => part.Repeated));
                        if (argument.Value.Default is not null && (occurrences.Any(part => part.Repeated) ||
                            compiled.Branches.All(branch => branch.Any(part => part.Name == argument.Key && part.Minimum > 0))))
                            Fail($"form '{form.Id}': defaults require an optional scalar argument.");
                    }
                    foreach (var option in compiled.Options.Values)
                    {
                        ValidateMetadata(option.Name, option.Definition, option.Repeated);
                        if (option.Definition.ValueName is { } valueName &&
                            (!Regex.IsMatch(valueName, @"^[A-Za-z][A-Za-z0-9_]*$") || form.Arguments.ContainsKey(valueName) ||
                             compiled.Options.Values.Count(other => other.Definition.ValueName == valueName) > 1))
                            Fail($"form '{form.Id}': option valueName '{valueName}' collides or is invalid.");
                        if (option.Definition.Choices is not null && option.Definition.ValueName is null)
                            Fail("Boolean switches cannot have choices.");
                        if (option.Definition.Default is not null && (option.Required || option.Repeated || option.Definition.ValueName is null))
                            Fail("Defaults require an optional, non-repeated valued option.");
                    }
                    forms.Add(compiled);
                }
                formLocation = null;
                var branches = forms.SelectMany(form => form.Branches).ToArray();
                for (var left = 0; left < branches.Length; left++)
                    for (var right = left + 1; right < branches.Length; right++)
                        if (Overlaps(branches[left], branches[right]) || NonDeterministicPrefix(branches[left], branches[right]))
                            Fail("Ambiguous positional grammar; distinguish forms by literals, not types, choices, or options.");
                var command = new CompiledCommand(definition, forms.AsReadOnly());
                foreach (var name in definition.Aliases.Prepend(definition.Name))
                    if (name.Equals("/help", StringComparison.OrdinalIgnoreCase) ||
                        !Regex.IsMatch(name, @"^/[A-Za-z][A-Za-z0-9-]*$") || !names.TryAdd(name, command))
                        Fail($"Invalid, reserved, or colliding command name '{name}'.");
                commands.Add(command);
            }
            catch (CommandConfigurationException exception)
            {
                throw new CommandConfigurationException($"{formLocation ?? $"$.commands[{commandIndex}], command '{definition.Name}'"}: {exception.Message}");
            }
            catch (ArgumentException exception)
            {
                throw new CommandConfigurationException($"{formLocation ?? $"$.commands[{commandIndex}], command '{definition.Name}'"}: {exception.Message}");
            }
        }
        _names = names.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _commands = commands.AsReadOnly();
        var helpForm = new CommandFormDefinition
        {
            Id = "help", Syntax = "[<command>]", Handler = "builtin.help",
            Arguments = new Dictionary<string, CommandValueDefinition>
            {
                ["command"] = new() { Description = "Registered command name or alias, with or without '/'.",
                    Choices = new() { Values = AllNames.ToArray(), Validation = "suggestions" } }
            }
        };
        _help = new(new CommandDefinition { Name = "/help", Description = "Show command help.", Forms = [helpForm] },
            [new CommandSyntaxCompiler(helpForm).Compile()]);
    }

    public IReadOnlyList<string> CommandNames => Array.AsReadOnly(new[] { "/help" }.Concat(_commands.Select(command => command.Definition.Name)).ToArray());
    internal IEnumerable<string> AllNames => new[] { "/help" }.Concat(_names.Keys);
    internal CompiledCommand? Find(string name) => name.Equals("/help", StringComparison.OrdinalIgnoreCase) ? _help : _names.GetValueOrDefault(name);
    internal ICommandHandler Handler(string id) => _handlers[id];
    internal ICommandValidator Validator(string id) => _validators[id];

    internal async ValueTask<CommandChoiceSnapshot> ChoicesAsync(CommandValueDefinition definition, CommandInvocation invocation,
        string name, bool repeated, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var choices = definition.Choices!;
        var mode = choices.Selection == "multiple" ? OptionPickerMode.Multiple : OptionPickerMode.Single;
        var snapshot = choices.Provider is { } provider
            ? await _providers[provider].GetChoicesAsync(new(invocation, name), cancellationToken)
            : new CommandChoiceSnapshot(choices.Values!.Select(value => new TerminalOption(value, value)).ToArray(), choices.CaseSensitive, mode);
        cancellationToken.ThrowIfCancellationRequested();
        if (snapshot is null || snapshot.Options is null)
            throw new InvalidOperationException($"Choice provider for '{name}' returned a null snapshot.");
        if (snapshot.Mode != mode || (snapshot.Mode == OptionPickerMode.Multiple && !repeated))
            throw new InvalidOperationException($"Choice provider for '{name}' returned incompatible selection cardinality.");
        var comparer = snapshot.CaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        if (snapshot.Options.Any(option => option is null || option.Value is null || option.Label is null || option.Value.Any(char.IsControl)) ||
            snapshot.Options.Select(option => option.Value).Distinct(comparer).Count() != snapshot.Options.Count)
            throw new InvalidOperationException($"Choice provider for '{name}' returned null, duplicate, or control-containing choices.");
        foreach (var option in snapshot.Options) CommandScalar.Convert(option.Value, definition);
        return snapshot with { Options = snapshot.Options.Select(option => option with
            { Label = new string(option.Label.Select(character => char.IsControl(character) ? ' ' : character).ToArray()) }).ToArray() };
    }

    public string GetHelp(string? target = null)
    {
        if (string.IsNullOrWhiteSpace(target))
            return "Commands:\n  /help [<command>] - Show command help.\n" +
                string.Join('\n', _commands.Select(command => $"  {command.Definition.Name} - {Safe(command.Definition.Description)}"));
        if (!target.StartsWith('/')) target = "/" + target;
        var command = Find(target) ?? throw new FormatException($"Unknown help target '{Safe(target)}'.");
        var text = new StringBuilder().AppendLine(Safe(command.Definition.Description));
        if (command.Definition.Aliases.Count > 0) text.AppendLine("Aliases: " + string.Join(", ", command.Definition.Aliases));
        foreach (var form in command.Forms)
        {
            text.AppendLine($"Usage: {command.Definition.Name} {form.Definition.Syntax}".TrimEnd());
            foreach (var argument in form.Definition.Arguments)
            {
                var parts = form.Branches.SelectMany(branch => branch).Where(part => part.IsCapture && part.Name == argument.Key);
                var cardinality = parts.Any(part => part.Repeated) ? "repeated" :
                    form.Branches.Any(branch => branch.All(part => part.Name != argument.Key)) ? "optional" : "required";
                text.AppendLine(Describe(argument.Key, argument.Value, cardinality));
            }
            foreach (var option in form.Options.Values)
                text.AppendLine(Describe(option.Name + (option.Definition.ValueName is { } valueName ? $" <{valueName}>" : "") +
                    (option.Definition.Aliases.Count > 0 ? $" ({string.Join(", ", option.Definition.Aliases)})" : ""),
                    option.Definition, (option.Required ? "required" : "optional") + (option.Repeated ? ", repeated" : "")));
            foreach (var example in form.Definition.Examples) text.AppendLine("Example: " + Safe(example));
        }
        return text.ToString().TrimEnd();
    }

    internal static string Safe(string text) => new(text.Where(character => !char.IsControl(character) || character is '\n' or '\t').ToArray());
    private static string Describe(string name, CommandValueDefinition definition, string cardinality) =>
        $"  {name}: {definition.Type}, {cardinality}{(definition.Default is null ? "" : ", default=" + Safe(definition.Default))}" +
        $"{(definition.Choices is null ? "" : ", choices=" + definition.Choices.Validation)}" +
        $"{(definition.Choices?.Values is { } values ? " [" + string.Join(", ", values.Select(Safe)) + "]" : "")}" +
        $"{(definition.Min is { } min ? ", min=" + min.ToString(System.Globalization.CultureInfo.InvariantCulture) : "")}" +
        $"{(definition.Max is { } max ? ", max=" + max.ToString(System.Globalization.CultureInfo.InvariantCulture) : "")}" +
        $"{(definition.MinLength is { } minLength ? ", minLength=" + minLength : "")}" +
        $"{(definition.MaxLength is { } maxLength ? ", maxLength=" + maxLength : "")} - {Safe(definition.Description)}";

    private void ValidateMetadata(string name, CommandValueDefinition definition, bool repeated)
    {
        if (definition.Type is not ("string" or "relativePath" or "integer" or "number" or "boolean")) Fail($"'{name}': unknown type.");
        if ((definition.Min is not null || definition.Max is not null) && definition.Type is not ("integer" or "number") ||
            (definition.MinLength is not null || definition.MaxLength is not null) && definition.Type is not ("string" or "relativePath") ||
            definition.MinLength < 0 || definition.MaxLength < 0 || definition.MinLength > definition.MaxLength ||
            definition.Min > definition.Max || definition.Min is { } min && !double.IsFinite(min) ||
            definition.Max is { } max && !double.IsFinite(max))
            Fail($"'{name}': invalid or incompatible bounds.");
        if (definition.Validator is { } validator && (string.IsNullOrWhiteSpace(validator) || !_validators.ContainsKey(validator))) Fail($"'{name}': validator '{validator}' is not registered.");
        if (definition.Choices is { } choices)
        {
            if ((choices.Values is null) == (choices.Provider is null) ||
                choices.Validation is not ("closed" or "suggestions") || choices.Selection is not ("single" or "multiple") ||
                choices.Selection == "multiple" && !repeated)
                Fail($"'{name}': invalid choice configuration/cardinality.");
            if (choices.Provider is { } provider && (string.IsNullOrWhiteSpace(provider) || !_providers.ContainsKey(provider))) Fail($"'{name}': provider '{provider}' is not registered.");
            if (choices.Values is { } values)
            {
                var comparer = choices.CaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
                if (values.Distinct(comparer).Count() != values.Count) Fail($"'{name}': duplicate choices.");
                if (values.Any(value => value.Any(char.IsControl))) Fail($"'{name}': choice values cannot contain terminal control characters.");
                foreach (var value in values) ConvertConfiguration(value, definition, name);
            }
        }
        if (definition.Default is { } defaultValue)
        {
            ConvertConfiguration(defaultValue, definition, name);
            if (definition.Validator is not null || definition.Choices is { Provider: not null, Validation: "closed" })
                Fail($"'{name}': defaults cannot depend on dynamic closed choices or custom validators.");
            if (definition.Choices is { Values: { } values, Validation: "closed" } defaultChoices &&
                !values.Contains(defaultValue, defaultChoices.CaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase))
                Fail($"'{name}': default is not a closed choice.");
        }
    }

    private static void ConvertConfiguration(string text, CommandValueDefinition definition, string name)
    {
        try { CommandScalar.Convert(text, definition); }
        catch (FormatException exception) { Fail($"'{name}': {exception.Message}"); }
    }

    private static void ValidateCollections(CommandConfiguration configuration)
    {
        if (configuration.Commands is null) Fail("$.commands cannot be null.");
        foreach (var command in configuration.Commands!)
        {
            if (command is null || command.Name is null || command.Description is null || command.Aliases is null ||
                command.Aliases.Any(alias => alias is null) || command.Forms is null)
                Fail("$.commands: command properties/collections cannot be null.");
            foreach (var form in command!.Forms)
            {
                if (form is null || form.Id is null || form.Syntax is null || form.Handler is null ||
                    form.Arguments is null || form.Options is null || form.Examples is null || form.Examples.Any(example => example is null))
                    Fail($"command '{command.Name}': form properties/collections cannot be null.");
                foreach (var definition in form!.Arguments.Values.Concat<CommandValueDefinition>(form.Options.Values))
                {
                    if (definition is null || definition.Type is null || definition.Description is null ||
                        definition.Choices is { } choices && (choices.Validation is null || choices.Selection is null ||
                            choices.Values?.Any(value => value is null) == true) ||
                        definition is CommandOptionDefinition option && (option.Aliases is null || option.Aliases.Any(alias => alias is null)))
                        Fail($"command '{command.Name}', form '{form.Id}': value metadata/collections cannot be null.");
                    if (definition.Min is { } min && !double.IsFinite(min) || definition.Max is { } max && !double.IsFinite(max))
                        Fail($"command '{command.Name}', form '{form.Id}': numeric bounds must be finite.");
                }
            }
        }
    }

    private static bool Overlaps(IReadOnlyList<SyntaxPart> left, IReadOnlyList<SyntaxPart> right)
    {
        var leftMin = left.Sum(part => part.Minimum);
        var rightMin = right.Sum(part => part.Minimum);
        var leftMax = left.Any(part => part.Repeated) ? int.MaxValue : left.Count;
        var rightMax = right.Any(part => part.Repeated) ? int.MaxValue : right.Count;
        if (Math.Max(leftMin, rightMin) > Math.Min(leftMax, rightMax)) return false;
        for (var index = 0; index < Math.Min(left.Count, right.Count); index++)
            if (!left[index].IsCapture && !right[index].IsCapture &&
                !left[index].Name.Equals(right[index].Name, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static bool NonDeterministicPrefix(IReadOnlyList<SyntaxPart> left, IReadOnlyList<SyntaxPart> right)
    {
        for (var index = 0; index < Math.Min(left.Count, right.Count); index++)
        {
            var a = left[index];
            var b = right[index];
            if (!a.IsCapture && !b.IsCapture && !a.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase)) return false;
            if (a.IsCapture != b.IsCapture || a.IsCapture && a.Name != b.Name) return true;
        }
        return false;
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string message) => throw new CommandConfigurationException(message);
}
