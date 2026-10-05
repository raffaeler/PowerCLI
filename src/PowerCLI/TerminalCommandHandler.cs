using System.Collections.Frozen;

namespace PowerCLI;

public sealed class TerminalCommandHandler(CommandRegistry registry) : ICommandDispatcher
{
    private readonly CommandRegistry _registry = registry;

    public async ValueTask<TerminalCommandResult> HandleAsync(string? input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(input) || !input.TrimStart().StartsWith('/')) return TerminalCommandResult.NotACommand;
        CompiledCommand? command = null;
        var root = CommandLineArguments.Tokenize(input, true).FirstOrDefault()?.Value;
        if (root is not null) command = _registry.Find(root);
        try
        {
            var tokens = CommandLineArguments.Tokenize(input);
            command = _registry.Find(tokens[0].Value);
            if (command is null) return Error($"Unknown command '{CommandRegistry.Safe(tokens[0].Value)}'. Type /help for available commands.", input);
            CommandInvocation? invocation = null;
            CompiledForm? matchedForm = null;
            var failures = new List<CommandDiagnostic>();
            foreach (var form in command.Forms)
                foreach (var branch in form.Branches)
                    try
                    {
                        invocation = CommandBinder.Bind(command, form, branch, tokens.Skip(1).ToArray(), input, false).Invocation;
                        matchedForm = form;
                    }
                    catch (FormatException exception)
                    {
                        failures.Add(exception is CommandInputException inputException
                            ? new(exception.Message, inputException.Start, inputException.Length, $"{command.Definition.Name}/{form.Definition.Id}")
                            : new(exception.Message, 0, input.Length, $"{command.Definition.Name}/{form.Definition.Id}"));
                    }
            if (invocation is null || matchedForm is null)
                return new(true, [CommandRegistry.Safe(string.Join(" ", failures.Select(failure => failure.Message).Distinct())), _registry.GetHelp(command.Definition.Name)],
                    Diagnostics: failures.AsReadOnly());
            if (invocation.Command == "/help")
                return TerminalCommandResult.Message(_registry.GetHelp(invocation["command"].Items.Count == 0 ? null : invocation["command"].String));
            var values = invocation.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            foreach (var entry in matchedForm.Definition.Arguments.Select(pair => (pair.Key, pair.Value,
                         matchedForm.Branches.SelectMany(branch => branch).Any(part => part.Name == pair.Key && part.Repeated)))
                     .Concat(matchedForm.Options.Values.Select(option => (option.Name, (CommandValueDefinition)option.Definition, option.Repeated))))
            {
                var (name, definition, repeated) = entry;
                var value = values[name];
                if (value.Items.Count == 0) continue;
                if (definition.Choices is { Validation: "closed" })
                {
                    var snapshot = await _registry.ChoicesAsync(definition, invocation, name, repeated, cancellationToken);
                    var comparer = snapshot.CaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
                    var canonical = new List<CommandScalar>();
                    foreach (var scalar in value.Items)
                    {
                        var choice = snapshot.Options.FirstOrDefault(option => comparer.Equals(option.Value, scalar.Text));
                        if (choice is null)
                        {
                            var source = invocation.Sources.GetValueOrDefault(name)?.FirstOrDefault(token => token.Value == scalar.Text);
                            return Error($"Invalid choice '{CommandRegistry.Safe(scalar.Text)}' for '{name}'.", input, command, source?.Start, source?.Length);
                        }
                        canonical.Add(CommandScalar.Convert(choice.Value, definition));
                    }
                    values[name] = value with { Items = canonical.AsReadOnly() };
                    invocation = invocation with { Values = values.ToFrozenDictionary(StringComparer.Ordinal) };
                }
            }
            // Validators see all converted, canonical values, not partially normalized values.
            foreach (var entry in matchedForm.Definition.Arguments.Select(pair => (pair.Key, pair.Value))
                         .Concat(matchedForm.Options.Values.Select(option => (option.Name, (CommandValueDefinition)option.Definition))))
                if (entry.Item2.Validator is { } validator && invocation[entry.Item1].Items.Count > 0 &&
                    await _registry.Validator(validator).ValidateAsync(invocation, entry.Item1, invocation[entry.Item1], cancellationToken) is { } message)
                    return Error(string.IsNullOrWhiteSpace(message) ? $"Validator '{validator}' rejected '{entry.Item1}' without an explanation." : message, input, command);
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _registry.Handler(invocation.HandlerId).ExecuteAsync(invocation, cancellationToken);
            if (result is null || !result.IsHandled)
                throw new InvalidOperationException($"Handler '{invocation.HandlerId}' must return a handled command result.");
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (CommandInputException exception) { return Error(exception.Message, input, command, exception.Start, exception.Length); }
        catch (Exception exception)
        {
            return Error($"Command failed: {exception.Message}", input, command);
        }
    }

    private TerminalCommandResult Error(string message, string input, CompiledCommand? command = null, int? start = null, int? length = null) =>
        new(true, command is null ? [CommandRegistry.Safe(message)] : [CommandRegistry.Safe(message), _registry.GetHelp(command.Definition.Name)],
            Diagnostics: [new(CommandRegistry.Safe(message), start ?? 0, length ?? input.Length)]);
}
