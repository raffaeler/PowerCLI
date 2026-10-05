namespace PowerCLI;

public sealed class TerminalCompletionResolver(CommandRegistry registry) : IOptionPickerResolver
{
    private readonly CommandRegistry _registry = registry;

    public async ValueTask<IReadOnlyList<TerminalCompletion>> ResolveAsync(string input, CancellationToken cancellationToken = default) =>
        (await ResolvePickerAsync(input, cancellationToken))?.Options.Select(option =>
            new TerminalCompletion(option.QuoteWhenInserted ? option.Value : option.Value.TrimEnd(), option.Label, option.IsSelected)).ToArray() ?? [];

    public ValueTask<OptionPicker?> ResolvePickerAsync(string input, CancellationToken cancellationToken = default) =>
        ResolvePickerAsync(new CompletionRequest(input, input.Length), cancellationToken);

    public async ValueTask<OptionPicker?> ResolvePickerAsync(CompletionRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var input = request.Input;
        if (request.Cursor < 0 || request.Cursor > input.Length) throw new ArgumentOutOfRangeException(nameof(request));
        if (!input.TrimStart().StartsWith('/')) return null;
        var tokens = CommandLineArguments.Tokenize(input, true);
        if (tokens.Count == 0 || request.Cursor < tokens[0].Start) return null;
        var active = tokens.FirstOrDefault(token => token.Start <= request.Cursor && token.Start + token.Length >= request.Cursor);
        var start = active?.Start ?? request.Cursor;
        var length = active?.Length ?? 0;
        var before = tokens.Where(token => token.Start < start).ToArray();
        var filter = active is null ? "" : CommandLineArguments.Unquote(input[start..request.Cursor]);
        if (before.Length == 0)
        {
            var roots = _registry.AllNames.Order(StringComparer.OrdinalIgnoreCase).Select(name =>
            {
                var command = _registry.Find(name);
                var acceptsEmpty = command?.Forms.Any(form => form.Branches.Any(branch => branch.Sum(part => part.Minimum) == 0) &&
                    form.Options.Values.All(option => !option.Required)) == true;
                return new TerminalOption(name + (!acceptsEmpty ? " " : ""),
                    name, QuoteWhenInserted: false);
            }).ToArray();
            return Picker(roots);
        }
        var compiled = _registry.Find(before[0].Value);
        if (compiled is null) return null;
        var candidates = new List<TerminalOption>();
        var append = false;
        OptionPickerMode mode = OptionPickerMode.Single;
        foreach (var form in compiled.Forms)
            foreach (var branch in form.Branches)
            {
                CommandBinding binding;
                try { binding = CommandBinder.Bind(compiled, form, branch, before.Skip(1).ToArray(), input, true); }
                catch (FormatException) { continue; }
                CommandValueDefinition? definition = null;
                string? name = null;
                var repeated = false;
                var sourceEquals = active is null ? -1 : input.IndexOf('=', active.Start, active.Length);
                var equals = sourceEquals >= 0 && request.Cursor > sourceEquals ? active!.Value.IndexOf('=') : -1;
                if (!binding.OptionsEnded && active?.Value.StartsWith('-') == true && equals >= 0 &&
                    form.OptionNames.TryGetValue(active.Value[..equals], out var equalOption) && equalOption.Definition.ValueName is not null)
                {
                    start = sourceEquals + 1;
                    length = active.Start + active.Length - start;
                    filter = CommandLineArguments.Unquote(input[start..request.Cursor]);
                    definition = equalOption.Definition;
                    name = equalOption.Name;
                    repeated = equalOption.Repeated;
                    append |= binding.Next is not null || form.Options.Values.Any(option => option.Required && !binding.UsedOptions.Contains(option.Name) && option.Name != equalOption.Name);
                }
                else if (binding.PendingOption is { } pending)
                {
                    definition = pending.Definition;
                    name = pending.Name;
                    repeated = pending.Repeated;
                    append |= binding.Next is not null || form.Options.Values.Any(option => option.Required && !binding.UsedOptions.Contains(option.Name));
                }
                else
                {
                    if (!binding.OptionsEnded && (filter.Length == 0 || filter.StartsWith('-')))
                    {
                        var usedElsewhere = new HashSet<string>(binding.UsedOptions, StringComparer.OrdinalIgnoreCase);
                        var suffixEnded = false;
                        foreach (var token in tokens.Where(token => token.Start > (active?.Start ?? request.Cursor)))
                        {
                            if (token.Value == "--") suffixEnded = true;
                            if (!suffixEnded && form.OptionNames.TryGetValue(token.Value.Split('=')[0], out var usedOption)) usedElsewhere.Add(usedOption.Name);
                        }
                        candidates.AddRange(form.OptionNames.Where(pair => pair.Value.Repeated || !binding.UsedOptions.Contains(pair.Value.Name))
                            .Where(pair => pair.Value.Repeated || !usedElsewhere.Contains(pair.Value.Name))
                            .Select(pair => new TerminalOption(pair.Key + (sourceEquals >= request.Cursor ||
                                pair.Value.Definition.ValueName is null && binding.Next is null &&
                                !form.Options.Values.Any(option => option.Required && option.Name != pair.Value.Name && !binding.UsedOptions.Contains(option.Name)) ? "" : " "),
                                pair.Key, QuoteWhenInserted: false)));
                        if (sourceEquals >= request.Cursor && active is not null) length = sourceEquals - active.Start;
                    }
                    if (!binding.OptionsEnded && filter.StartsWith('-')) continue;
                    if (binding.Next is { IsCapture: false } literal)
                    {
                        var needsSpace = branch.IndexOfPart(literal) < branch.Count - 1 ||
                            form.Options.Values.Any(option => option.Required && !binding.UsedOptions.Contains(option.Name));
                        candidates.Add(new(literal.Name + (needsSpace ? " " : ""), literal.Name, QuoteWhenInserted: false));
                        continue;
                    }
                    if (binding.Next is { IsCapture: true } capture)
                    {
                        definition = form.Definition.Arguments[capture.Name];
                        name = capture.Name;
                        repeated = capture.Repeated;
                        append |= !capture.Repeated && (branch.IndexOfPart(capture) < branch.Count - 1 ||
                            form.Options.Values.Any(option => option.Required && !binding.UsedOptions.Contains(option.Name)));
                    }
                }
                if (definition?.Choices is null || name is null) continue;
                CommandChoiceSnapshot snapshot;
                try { snapshot = await _registry.ChoicesAsync(definition, binding.Invocation, name, repeated, cancellationToken); }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    return new(input[..start], [], ReplacementStart: start, ReplacementLength: length, Filter: filter,
                        Error: $"Completion failed: {CommandRegistry.Safe(exception.Message)}");
                }
                if (snapshot.Mode == OptionPickerMode.Multiple)
                {
                    mode = OptionPickerMode.Multiple;
                    candidates.Clear();
                    if (definition is CommandOptionDefinition)
                    {
                        var option = binding.PendingOption ?? form.OptionNames[active!.Value[..equals]];
                        var optionToken = binding.PendingOption is null ? active! : before[^1];
                        start = optionToken.Start;
                        length = (active?.Start + active?.Length ?? request.Cursor) - start;
                        var segments = new List<(int Start, int End, string Value)>();
                        for (var index = 1; index < tokens.Count; index++)
                        {
                            var token = tokens[index];
                            if (token.Value == "--") break;
                            var separator = token.Value.IndexOf('=');
                            var optionName = separator < 0 ? token.Value : token.Value[..separator];
                            if (!form.OptionNames.TryGetValue(optionName, out var known) || known.Name != option.Name) continue;
                            var end = token.Start + token.Length;
                            var value = separator >= 0 ? token.Value[(separator + 1)..] : "";
                            if (separator < 0 && index + 1 < tokens.Count && !tokens[index + 1].Value.StartsWith('-'))
                            {
                                var valueToken = tokens[++index];
                                end = valueToken.Start + valueToken.Length;
                                value = valueToken.Value;
                            }
                            segments.Add((token.Start, end, value));
                        }
                        foreach (var previous in segments.Where(segment => segment.Start < start).Reverse())
                        {
                            if (input[previous.End..start].Any(character => !char.IsWhiteSpace(character))) break;
                            length += start - previous.Start;
                            start = previous.Start;
                        }
                        foreach (var following in segments.Where(segment => segment.Start >= start + length))
                        {
                            if (input[(start + length)..following.Start].Any(character => !char.IsWhiteSpace(character))) break;
                            length = following.End - start;
                        }
                        var supplied = segments.Where(segment => segment.Start >= start && segment.End <= start + length)
                            .Select(segment => segment.Value).ToHashSet(snapshot.CaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
                        return new(input[..start], snapshot.Options.Select(candidate => candidate with
                            { IsSelected = candidate.IsSelected || supplied.Contains(candidate.Value) }).ToArray(), mode,
                            ReplacementStart: start, ReplacementLength: length, Filter: filter, RepeatedOptionName: option.Name);
                    }
                    // Replace only the contiguous repeated positional segment, never surrounding options.
                    if (binding.Invocation.Sources.TryGetValue(name, out var existing) && existing.Count > 0 &&
                        existing[^1].Start + existing[^1].Length <= start &&
                        !tokens.Any(token => token.Start > existing[0].Start && token.Start < start &&
                            (token.Value == "--" || !binding.OptionsEnded && token.Value.StartsWith('-'))))
                    {
                        start = existing[0].Start;
                        length = (active?.Start + active?.Length ?? request.Cursor) - start;
                    }
                    foreach (var trailing in tokens.Where(token => token.Start > (active?.Start ?? request.Cursor)))
                    {
                        if (!binding.OptionsEnded && trailing.Value.StartsWith('-')) break;
                        length = trailing.Start + trailing.Length - start;
                    }
                    var selected = CommandLineArguments.Tokenize(input.Substring(start, length), true).Select(token => token.Value)
                        .ToHashSet(snapshot.CaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
                    candidates.AddRange(snapshot.Options.Select(option => option with { IsSelected = option.IsSelected || selected.Contains(option.Value) }));
                    return Picker(candidates, mode);
                }
                candidates.AddRange(snapshot.Options);
            }
        return candidates.Count == 0 ? null : Picker(candidates, mode, append);

        OptionPicker Picker(IEnumerable<TerminalOption> options, OptionPickerMode pickerMode = OptionPickerMode.Single, bool appendSpace = false) =>
            new(input[..start], options.GroupBy(option => option.Value, StringComparer.Ordinal).Select(group => group.First()).ToArray(),
                pickerMode, ReplacementStart: start, ReplacementLength: length, Filter: filter, AppendSpace: appendSpace);
    }
}
