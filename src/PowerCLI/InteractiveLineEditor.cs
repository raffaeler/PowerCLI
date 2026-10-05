using System.Text;

namespace PowerCLI;

/// <summary>Edits input using raw keys with session history and filtered completion menus.</summary>
public sealed class InteractiveLineEditor : ILineEditor
{
    private readonly ITerminalConsole _console;
    private readonly IOptionPickerResolver? _resolver;
    private readonly IConsoleInteractionSurface? _surface;
    private readonly List<string> _history = [];

    public InteractiveLineEditor(
        ITerminalConsole console,
        IOptionPickerResolver? completionResolver = null,
        IConsoleInteractionSurface? surface = null)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _resolver = completionResolver;
        _surface = surface ?? (console is SystemTerminalConsole ? new SystemConsoleInteractionSurface() : null);
    }

    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default) =>
        ReadLineAsync(string.Empty, cancellationToken);

    public async ValueTask<string?> ReadLineAsync(string prompt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        cancellationToken.ThrowIfCancellationRequested();
        string? input;
        if (_console.IsInputRedirected || _console.IsOutputRedirected)
        {
            _console.Write(prompt);
            input = await _console.ReadLineAsync(cancellationToken);
        }
        else
        {
            input = await ReadInteractiveAsync(prompt, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(input) &&
            (_history.Count == 0 || !string.Equals(_history[^1], input, StringComparison.Ordinal)))
        {
            _history.Add(input);
        }

        return input;
    }

    public async ValueTask<IReadOnlyList<TerminalOption>> CompleteAsync(string input, CancellationToken cancellationToken = default) =>
        _resolver is null ? [] : (await _resolver.ResolvePickerAsync(input, cancellationToken))?.Options ?? [];

    public async ValueTask<IReadOnlyList<TerminalOption>> PickAsync(
        string title,
        IReadOnlyList<TerminalOption> options,
        bool multiSelect = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_console.IsInputRedirected || _console.IsOutputRedirected || options.Count == 0)
        {
            return [];
        }

        _console.WriteLine(title);
        _console.WriteLine(multiSelect
            ? "Up/Down: navigate | Space: toggle | Enter: accept | Esc: cancel"
            : "Up/Down: navigate | Enter: accept | Esc: cancel");
        var picker = new OptionPicker(string.Empty, options, multiSelect ? OptionPickerMode.Multiple : OptionPickerMode.Single);
        var line = await ReadInteractiveAsync(string.Empty, cancellationToken, picker);
        return options.Where(option => CommandLineArguments.Parse(line ?? string.Empty).Contains(option.Value, StringComparer.Ordinal)).ToArray();
    }

    private async ValueTask<string?> ReadInteractiveAsync(
        string prompt,
        CancellationToken cancellationToken,
        OptionPicker? fixedPicker = null)
    {
        var surface = _surface ?? throw new InvalidOperationException("Interactive input requires an IConsoleInteractionSurface.");
        var line = new StringBuilder();
        var cursor = 0;
        var historyIndex = _history.Count;
        var draft = string.Empty;
        var draftCursor = 0;
        var activeIndex = 0;
        var firstVisible = 0;
        var previousRows = 0;
        var startLeft = surface.CursorLeft;
        var startTop = ReserveRows(surface, startLeft);
        var pickerClosed = false;
        var selectedValues = new HashSet<string>(StringComparer.Ordinal);
        var selectionOverrides = new HashSet<string>(StringComparer.Ordinal);
        string? selectionPrefix = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var picker = pickerClosed ? null : fixedPicker ??
                (_resolver is null ? null : await _resolver.ResolvePickerAsync(new CompletionRequest(line.ToString(), cursor), cancellationToken));
            if (picker is not null && !string.Equals(selectionPrefix, picker.InputPrefix, StringComparison.OrdinalIgnoreCase))
            {
                selectedValues.Clear();
                selectionOverrides.Clear();
                selectedValues.UnionWith(picker.Options.Where(option => option.IsSelected).Select(option => option.Value));
                selectionPrefix = picker.InputPrefix;
                activeIndex = 0;
                firstVisible = 0;
            }
            if (picker is { Mode: OptionPickerMode.Multiple })
                foreach (var option in picker.Options.Where(option => !selectionOverrides.Contains(option.Value)))
                {
                    if (option.IsSelected) selectedValues.Add(option.Value);
                    else selectedValues.Remove(option.Value);
                }

            var matches = Matches(picker, line.ToString());
            activeIndex = Math.Clamp(activeIndex, 0, Math.Max(0, matches.Count - 1));
            var height = Math.Max(3, picker?.ViewportHeight ?? 6);
            var viewport = Viewport(matches.Count, activeIndex, firstVisible, height);
            firstVisible = viewport.First;
            previousRows = Render(surface, prompt, line.ToString(), cursor, picker, matches, activeIndex,
                viewport, selectedValues, startLeft, ref startTop, previousRows);

            var key = await surface.ReadKeyAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (key.Key == ConsoleKey.R && (key.Modifiers & ConsoleModifiers.Control) != 0)
            {
                if (fixedPicker is null && matches.Count == 0 && historyIndex < _history.Count)
                {
                    _history.RemoveAt(historyIndex);
                    ApplyHistoryEntry();
                }

                continue;
            }

            if (key.Key == ConsoleKey.Escape && picker is not null)
            {
                if (fixedPicker is not null)
                {
                    Finish(surface, startLeft, startTop, prompt, string.Empty, previousRows);
                    return null;
                }

                pickerClosed = true;
                continue;
            }

            if (fixedPicker is null && matches.Count == 0 && key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow)
            {
                if (key.Key == ConsoleKey.UpArrow && historyIndex > 0)
                {
                    if (historyIndex == _history.Count)
                    {
                        draft = line.ToString();
                        draftCursor = cursor;
                    }

                    historyIndex--;
                    ApplyHistoryEntry();
                }
                else if (key.Key == ConsoleKey.DownArrow && historyIndex < _history.Count)
                {
                    historyIndex++;
                    ApplyHistoryEntry();
                }

                continue;
            }

            if (picker is not null && matches.Count > 0 && key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow)
            {
                activeIndex = key.Key == ConsoleKey.UpArrow
                    ? (activeIndex + matches.Count - 1) % matches.Count
                    : (activeIndex + 1) % matches.Count;
                continue;
            }

            if (picker is { Mode: OptionPickerMode.Multiple } && matches.Count > 0 && key.Key == ConsoleKey.Spacebar)
            {
                selectionOverrides.Add(matches[activeIndex].Value);
                if (!selectedValues.Add(matches[activeIndex].Value))
                {
                    selectedValues.Remove(matches[activeIndex].Value);
                }

                continue;
            }

            if (key.Key == ConsoleKey.Enter && picker is not null && matches.Count > 0)
            {
                var replacementStart = picker.ReplacementStart ?? picker.InputPrefix.Length;
                var replacementLength = picker.ReplacementLength ?? line.Length - replacementStart;
                if (fixedPicker is null && picker.Mode == OptionPickerMode.Single && !picker.AppendSpace &&
                    replacementStart + replacementLength == line.Length &&
                    picker.Options.Any(option => string.Equals(Format(option), line.ToString(replacementStart, replacementLength), StringComparison.OrdinalIgnoreCase)))
                {
                    Finish(surface, startLeft, startTop, prompt, line.ToString(), previousRows);
                    return line.ToString();
                }

                var values = picker.Mode == OptionPickerMode.Single
                    ? Format(matches[activeIndex])
                    : string.Join(' ', picker.Options.Where(option => selectedValues.Contains(option.Value))
                        .Select(option => (picker.RepeatedOptionName is { } optionName ? optionName + " " : "") + Format(option)));
                if (replacementStart + replacementLength < line.Length && char.IsWhiteSpace(line[replacementStart + replacementLength]))
                    values = values.TrimEnd();
                if (picker.AppendSpace && replacementStart + replacementLength == line.Length && !values.EndsWith(' ')) values += " ";
                line.Remove(replacementStart, replacementLength).Insert(replacementStart, values);
                cursor = replacementStart + values.Length;
                if (fixedPicker is not null)
                {
                    Finish(surface, startLeft, startTop, prompt, line.ToString(), previousRows);
                    return line.ToString();
                }

                var next = _resolver is null ? null : await _resolver.ResolvePickerAsync(new CompletionRequest(line.ToString(), cursor), cancellationToken);
                pickerClosed = next is null || string.Equals(next.InputPrefix, picker.InputPrefix, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    Finish(surface, startLeft, startTop, prompt, line.ToString(), previousRows);
                    return line.ToString();
                case ConsoleKey.Escape:
                    line.Clear();
                    cursor = 0;
                    pickerClosed = true;
                    activeIndex = 0;
                    firstVisible = 0;
                    selectedValues.Clear();
                    selectionOverrides.Clear();
                    selectionPrefix = null;
                    break;
                case ConsoleKey.LeftArrow when cursor > 0:
                    cursor--;
                    pickerClosed = false;
                    break;
                case ConsoleKey.RightArrow when cursor < line.Length:
                    cursor++;
                    pickerClosed = false;
                    break;
                case ConsoleKey.Home:
                    cursor = 0;
                    pickerClosed = false;
                    break;
                case ConsoleKey.End:
                    cursor = line.Length;
                    pickerClosed = false;
                    break;
                case ConsoleKey.Backspace when cursor > 0:
                    line.Remove(--cursor, 1);
                    pickerClosed = false;
                    break;
                case ConsoleKey.Delete when cursor < line.Length:
                    line.Remove(cursor, 1);
                    pickerClosed = false;
                    break;
                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        line.Insert(cursor++, key.KeyChar);
                        pickerClosed = false;
                        activeIndex = 0;
                    }

                    break;
            }
        }

        void ApplyHistoryEntry()
        {
            var isDraft = historyIndex == _history.Count;
            line.Clear().Append(isDraft ? draft : _history[historyIndex]);
            cursor = isDraft ? draftCursor : line.Length;
            pickerClosed = true;
            activeIndex = 0;
            firstVisible = 0;
            selectedValues.Clear();
            selectionOverrides.Clear();
            selectionPrefix = null;
        }
    }

    private static string Format(TerminalOption option) =>
        option.QuoteWhenInserted ? CommandLineArguments.QuoteIfNeeded(option.Value) : option.Value;

    private static IReadOnlyList<TerminalOption> Matches(OptionPicker? picker, string input)
    {
        if (picker is null || !input.StartsWith(picker.InputPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var filter = picker.Filter ?? CommandLineArguments.Unquote(input[picker.InputPrefix.Length..].Trim());
        return picker.Options.Where(option => option.Value.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            option.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private static PickerViewport Viewport(int count, int active, int first, int height)
    {
        if (count <= height)
        {
            return new(0, count, false, false);
        }

        first = Math.Clamp(first, 0, count - height + 1);
        if (active < first)
        {
            first = active;
        }

        while (true)
        {
            var above = first > 0;
            var capacity = height - (above ? 1 : 0);
            var below = first + capacity < count;
            var last = Math.Min(count, first + capacity - (below ? 1 : 0));
            if (active < last)
            {
                return new(first, last, above, below);
            }

            first++;
        }
    }

    private static int ReserveRows(IConsoleInteractionSurface surface, int left)
    {
        if (surface.UsesVirtualCursor)
        {
            return surface.CursorTop;
        }

        for (var row = 0; row < 6; row++)
        {
            surface.WriteLine();
        }

        var top = Math.Max(0, surface.CursorTop - 6);
        surface.SetCursorPosition(left, top);
        return top;
    }

    private static int Render(
        IConsoleInteractionSurface surface, string prompt, string line, int cursor,
        OptionPicker? picker, IReadOnlyList<TerminalOption> matches, int active,
        PickerViewport viewport, IReadOnlySet<string> selected, int left, ref int top, int previousRows)
    {
        var height = Math.Max(3, picker?.ViewportHeight ?? 6);
        if (picker is not null && !surface.UsesVirtualCursor &&
            (!surface.EnsureBufferHeight(top + Math.Max(16, height) + 2) || top + height >= surface.BufferHeight))
        {
            for (var row = 0; row < height; row++)
            {
                surface.WriteLine();
            }

            top = Math.Max(0, surface.CursorTop - height);
            previousRows = 0;
        }

        ClearRows(surface, left, top, previousRows + 1);
        surface.SetCursorPosition(left, top);
        var width = Math.Max(1, surface.WindowWidth - left - 1);
        var input = prompt + line;
        // Keep the insertion point visible without allowing a long prompt to wrap into the menu.
        var offset = Math.Max(0, prompt.Length + cursor - width + 1);
        surface.Write(input.Substring(offset, Math.Min(width, input.Length - offset)));
        var rows = 0;
        if (picker?.Error is { } error)
        {
            surface.WriteLine();
            surface.Write(SafeLabel(error)[..Math.Min(error.Length, width)]);
            rows = 1;
        }
        if (picker is not null && matches.Count > 0)
        {
            surface.WriteLine();
            var entries = new List<(string Text, bool Selected)>();
            if (viewport.Above)
            {
                entries.Add(("...", false));
            }

            for (var index = viewport.First; index < viewport.Last; index++)
            {
                var option = matches[index];
                var marker = picker.Mode == OptionPickerMode.Multiple ? (selected.Contains(option.Value) ? "[X] " : "[ ] ") : string.Empty;
                entries.Add((marker + SafeLabel(option.Label), index == active));
            }

            if (viewport.Below)
            {
                entries.Add(("...", false));
            }

            foreach (var entry in entries)
            {
                if (!surface.UsesVirtualCursor)
                {
                    surface.SetCursorPosition(left, top + rows + 1);
                }

                var text = entry.Text[..Math.Min(entry.Text.Length, width)].PadRight(width);
                surface.Write(text, entry.Selected);
                rows++;
                if (surface.UsesVirtualCursor && rows < entries.Count)
                {
                    surface.WriteLine();
                }
            }

            if (surface.UsesVirtualCursor)
            {
                top = Math.Max(0, surface.CursorTop - rows);
            }
        }

        surface.SetCursorPosition(left + Math.Min(width - 1, prompt.Length + cursor - offset), top);
        return rows;
    }

    private static string SafeLabel(string label) =>
        new(label.Select(character => char.IsControl(character) ? ' ' : character).ToArray());

    private static void ClearRows(IConsoleInteractionSurface surface, int left, int top, int rows)
    {
        var limit = surface.UsesVirtualCursor ? top + rows : Math.Min(surface.BufferHeight, top + rows);
        for (var row = top; row < limit; row++)
        {
            surface.SetCursorPosition(left, row);
            surface.Write(new string(' ', Math.Max(1, surface.WindowWidth - left - 1)));
        }
    }

    private static void Finish(IConsoleInteractionSurface surface, int left, int top, string prompt, string line, int rows)
    {
        ClearRows(surface, left, top + 1, rows);
        surface.SetCursorPosition(Math.Min(surface.WindowWidth - 1, left + prompt.Length + line.Length), top);
        surface.WriteLine();
    }
}
