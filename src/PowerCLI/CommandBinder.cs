using System.Collections.Frozen;

namespace PowerCLI;

internal static class CommandBinder
{
    internal static CommandBinding Bind(CompiledCommand command, CompiledForm form, IReadOnlyList<SyntaxPart> branch,
        IReadOnlyList<CommandToken> tokens, string input, bool partial)
    {
        var positional = new List<CommandToken>();
        var sources = new Dictionary<string, List<CommandToken>>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ended = false;
        CompiledOption? pending = null;
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (!ended && token.Value == "--") { ended = true; continue; }
            if (!ended && token.Value.StartsWith('-'))
            {
                var equals = token.Value.IndexOf('=');
                var name = equals < 0 ? token.Value : token.Value[..equals];
                if (!form.OptionNames.TryGetValue(name, out var option)) throw Invalid($"Unknown option '{name}'.", token);
                if (!used.Add(option.Name) && !option.Repeated) throw Invalid($"Duplicate option '{option.Name}'.", token);
                if (option.Definition.ValueName is null)
                {
                    if (equals >= 0) throw Invalid($"Switch '{name}' does not take a value.", token);
                    Add(option.Name, token with { Value = "true" });
                }
                else if (equals >= 0)
                {
                    var rawEquals = input.IndexOf('=', token.Start, token.Length);
                    var valueStart = rawEquals + 1;
                    Add(option.Name, new(token.Value[(equals + 1)..], valueStart, token.Start + token.Length - valueStart));
                }
                else if (index + 1 < tokens.Count && !tokens[index + 1].Value.StartsWith('-'))
                    Add(option.Name, tokens[++index]);
                else if (partial && index + 1 == tokens.Count) pending = option;
                else throw Invalid($"Option '{name}' requires a value (use = for leading-dash values).", token);
            }
            else positional.Add(token);
        }
        var literals = new List<string>();
        var position = 0;
        SyntaxPart? next = null;
        foreach (var part in branch)
        {
            if (position >= positional.Count)
            {
                next ??= part;
                if (!partial && part.Minimum > 0) throw new CommandInputException($"Missing {(part.IsCapture ? "<" + part.Name + ">" : part.Name)}.", input.Length, 0);
                continue;
            }
            if (!part.IsCapture)
            {
                if (!part.Name.Equals(positional[position].Value, StringComparison.OrdinalIgnoreCase))
                    throw Invalid($"Expected '{part.Name}'.", positional[position]);
                literals.Add(part.Name);
                position++;
            }
            else
            {
                Add(part.Name, positional[position++]);
                if (part.Repeated)
                {
                    while (position < positional.Count) Add(part.Name, positional[position++]);
                    next = part;
                }
            }
        }
        if (position < positional.Count) throw Invalid("Unexpected extra positional value.", positional[position]);
        if (!partial)
            foreach (var option in form.Options.Values.Where(option => option.Required && !used.Contains(option.Name)))
                throw new CommandInputException($"Missing required option '{option.Name}'.", input.Length, 0);
        var values = new Dictionary<string, CommandValue>(StringComparer.Ordinal);
        foreach (var argument in form.Definition.Arguments) Convert(argument.Key, argument.Value, false);
        foreach (var option in form.Options.Values) Convert(option.Name, option.Definition, option.Definition.ValueName is null);
        var invocation = new CommandInvocation(command.Definition.Name, form.Definition.Id, form.Definition.Handler, input,
            literals.AsReadOnly(), values.ToFrozenDictionary(StringComparer.Ordinal),
            sources.ToFrozenDictionary(pair => pair.Key, pair => (IReadOnlyList<CommandToken>)pair.Value.AsReadOnly(), StringComparer.Ordinal));
        return new(invocation, next, pending, used, ended);

        void Add(string name, CommandToken token)
        {
            if (!sources.TryGetValue(name, out var list)) sources.Add(name, list = []);
            list.Add(token);
        }

        void Convert(string name, CommandValueDefinition definition, bool flag)
        {
            if (sources.TryGetValue(name, out var list))
                values.Add(name, new(Array.AsReadOnly(list.Select(token => ConvertToken(token, definition)).ToArray()), true));
            else if (definition.Default is { } defaultValue)
                values.Add(name, new(Array.AsReadOnly(new[] { CommandScalar.Convert(defaultValue, definition) }), false, true));
            else if (flag) values.Add(name, new(Array.AsReadOnly(new[] { new CommandScalar("boolean", "false", Boolean: false) }), false));
            else values.Add(name, new(Array.Empty<CommandScalar>(), false));
        }
    }

    private static CommandScalar ConvertToken(CommandToken token, CommandValueDefinition definition)
    {
        try { return CommandScalar.Convert(token.Value, definition); }
        catch (FormatException exception) { throw Invalid(exception.Message, token); }
    }

    private static CommandInputException Invalid(string message, CommandToken token) => new(message, token.Start, token.Length);
}
