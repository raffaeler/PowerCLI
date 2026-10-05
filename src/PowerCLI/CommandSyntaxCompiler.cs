using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace PowerCLI;

internal sealed class CommandSyntaxCompiler
{
    private readonly CommandFormDefinition _form;
    private readonly List<string> _tokens;
    private readonly List<int> _offsets;
    private readonly Dictionary<string, CompiledOption> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _captures = new(StringComparer.Ordinal);
    private int _index;
    private int _positionalGroupDepth;

    public CommandSyntaxCompiler(CommandFormDefinition form)
    {
        _form = form;
        var matches = Regex.Matches(form.Syntax, @"<[^<>]*>|[()[\]|*+]|[^\s()[\]|*+<>]+");
        _tokens = matches.Select(match => match.Value).ToList();
        _offsets = matches.Select(match => match.Index).ToList();
        if (Regex.Replace(form.Syntax, @"<[^<>]*>|[()[\]|*+]|[^\s()[\]|*+<>]+", "").Any(c => !char.IsWhiteSpace(c)))
            Fail("Invalid syntax character.");
    }

    public CompiledForm Compile()
    {
        var branches = Sequence(null);
        if (_index != _tokens.Count) Fail("Unexpected syntax token.");
        if (!_captures.SetEquals(_form.Arguments.Keys)) Fail("Every positional capture needs exactly one used argument definition.");
        if (!_options.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(_form.Options.Keys)) Fail("Every option needs exactly one used definition.");
        foreach (var branch in branches)
        {
            if (branch.Take(Math.Max(0, branch.Count - 1)).Any(part => part.Repeated))
                Fail("Repeated positional captures must be trailing.");
        }
        var names = new Dictionary<string, CompiledOption>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in _options.Values)
            foreach (var name in option.Definition.Aliases.Prepend(option.Name))
            {
                if (!Regex.IsMatch(name, @"^(--[A-Za-z][A-Za-z0-9-]*|-[A-Za-z])$") || !names.TryAdd(name, option))
                    Fail($"Invalid or colliding option alias '{name}'.");
            }
        return new(_form, branches.Select(branch => (IReadOnlyList<SyntaxPart>)branch.AsReadOnly()).ToArray(),
            _options.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase), names.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
    }

    private List<List<SyntaxPart>> Sequence(string? closing)
    {
        var result = new List<List<SyntaxPart>> { new() };
        while (_index < _tokens.Count && _tokens[_index] != closing && _tokens[_index] != "|")
        {
            var token = _tokens[_index++];
            List<List<SyntaxPart>> fragment;
            if (token is "[" or "(")
            {
                var close = token == "[" ? "]" : ")";
                var optionStart = _index < _tokens.Count && _tokens[_index].StartsWith('-');
                if (optionStart)
                {
                    var optionName = _tokens[_index++];
                    ReadOption(optionName, token == "(");
                    Expect(close);
                    var repeat = Take("*") || Take("+");
                    if (token == "[" && repeat && _tokens[_index - 1] != "*") Fail("Optional option repetition uses [...]*.");
                    if (token == "(" && (!repeat || _tokens[_index - 1] != "+")) Fail("Required option groups use (...)+.");
                    _options[optionName] = _options[optionName] with { Repeated = repeat };
                    if (repeat && _options[optionName].Definition.ValueName is null) Fail("Only valued options may repeat.");
                    fragment = [new()];
                }
                else
                {
                    _positionalGroupDepth++;
                    fragment = Sequence(close);
                    while (Take("|")) fragment.AddRange(Sequence(close));
                    Expect(close);
                    _positionalGroupDepth--;
                    if (fragment.Count > 1 && fragment.Any(branch => branch.Count == 0 || branch[0].IsCapture))
                        Fail("Alternatives must start with distinguishing literals.");
                    if (fragment.All(branch => branch.Count == 0)) Fail("Empty positional groups are not supported.");
                    if (token == "[") fragment.Add(new());
                    if (_index < _tokens.Count && _tokens[_index] is "*" or "+") Fail("Only captures and option groups may repeat.");
                }
            }
            else if (token.StartsWith("--"))
            {
                ReadOption(token, true);
                fragment = [new()];
            }
            else if (token.StartsWith('<'))
            {
                var name = token[1..^1];
                if (!Regex.IsMatch(name, @"^[A-Za-z][A-Za-z0-9_]*$") || !_captures.Add(name)) Fail($"Invalid or duplicated capture '{name}'.");
                var repeated = Take("*") || Take("+");
                fragment = [[new(name, true, repeated && _tokens[_index - 1] == "*" ? 0 : 1, repeated)]];
            }
            else
            {
                if (!Regex.IsMatch(token, @"^[A-Za-z0-9][A-Za-z0-9_.-]*$")) Fail($"Invalid literal '{token}'.");
                fragment = [[new(token)]];
            }
            result = result.SelectMany(left => fragment.Select(right => left.Concat(right).ToList())).ToList();
            if (result.Count > 256) Fail("Syntax exceeds the 256-branch limit.");
        }
        return result;
    }

    private void ReadOption(string name, bool required)
    {
        if (_positionalGroupDepth > 0) Fail("Named options cannot be nested in positional groups or alternatives.");
        if (!Regex.IsMatch(name, @"^--[A-Za-z][A-Za-z0-9-]*$")) Fail("Canonical options use long --names; short names are explicit aliases.");
        if (!_form.Options.TryGetValue(name, out var definition)) Fail($"Missing option definition '{name}'.");
        if (!_options.TryAdd(name, new(name, definition!, required, false))) Fail($"Duplicated option '{name}'.");
        string? capture = null;
        if (_index < _tokens.Count && _tokens[_index].StartsWith('<')) capture = _tokens[_index++][1..^1];
        if (capture != definition!.ValueName || (capture is null && definition.Type != "boolean"))
            Fail($"Option '{name}' must agree with valueName; switches have boolean type.");
    }

    private bool Take(string token)
    {
        if (_index >= _tokens.Count || _tokens[_index] != token) return false;
        _index++;
        return true;
    }

    private void Expect(string token) { if (!Take(token)) Fail($"Expected '{token}'."); }
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private void Fail(string message) => throw new CommandConfigurationException(
        $"form '{_form.Id}', syntax offset {(_index < _offsets.Count ? _offsets[_index] : _form.Syntax.Length)}: {message}");
}
