using System.Text;
using System.Text.RegularExpressions;

namespace PowerCLI;

/// <summary>A visual style for terminal text. ANSI conversion is optional and host controlled.</summary>
public enum TerminalTextStyle
{
    Plain,
    Bold,
    Italic,
    Underline,
    Heading,
    Code,
    Muted
}

/// <summary>A styled segment of rendered Markdown.</summary>
public sealed record TerminalTextSpan(string Text, TerminalTextStyle Style = TerminalTextStyle.Plain);

/// <summary>Converts Markdown into console-friendly styled spans without requiring a console.</summary>
public sealed class IncrementalMarkdownConsoleRenderer
{
    private static readonly Regex InlinePattern = new(
        @"(`[^`]+`)|(\*\*[^*]+\*\*)|(__[^_]+__)|(\*[^*]+\*)|(_[^_]+_)|(!?\[[^\]]+\]\([^)]+\))",
        RegexOptions.Compiled);
    private readonly StringBuilder _pending = new();
    private bool _inCodeFence;

    /// <summary>Adds a model delta and returns readable output chunks when a boundary is reached.</summary>
    public IReadOnlyList<IReadOnlyList<TerminalTextSpan>> Append(string delta)
    {
        ArgumentNullException.ThrowIfNull(delta);
        _pending.Append(delta.Replace("<crlf>", "\r\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<cr>", "\r", StringComparison.OrdinalIgnoreCase));
        var chunks = new List<IReadOnlyList<TerminalTextSpan>>();

        while (TryTakeBoundary(out var text))
        {
            chunks.Add(Render(text));
        }

        return chunks;
    }

    /// <summary>Flushes all buffered model output.</summary>
    public IReadOnlyList<TerminalTextSpan> Flush()
    {
        var value = _pending.ToString();
        _pending.Clear();
        return value.Length == 0 ? [] : Render(value);
    }

    /// <summary>Renders complete Markdown content into styled text spans.</summary>
    public IReadOnlyList<TerminalTextSpan> Render(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var spans = new List<TerminalTextSpan>();
        foreach (var line in markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                _inCodeFence = !_inCodeFence;
                continue;
            }

            if (_inCodeFence || line.StartsWith("    ", StringComparison.Ordinal))
            {
                spans.Add(new(line.TrimStart() + Environment.NewLine, TerminalTextStyle.Code));
                continue;
            }

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                spans.Add(new(trimmed.TrimStart('#', ' ') + Environment.NewLine, TerminalTextStyle.Heading));
            }
            else if (trimmed is "---" or "***" or "___")
            {
                spans.Add(new("────────────────" + Environment.NewLine, TerminalTextStyle.Muted));
            }
            else if (trimmed.StartsWith(">", StringComparison.Ordinal))
            {
                spans.Add(new("> ", TerminalTextStyle.Muted));
                AddInline(spans, trimmed.TrimStart('>', ' '));
                spans.Add(new(Environment.NewLine));
            }
            else
            {
                AddInline(spans, FormatBlockPrefix(trimmed));
                spans.Add(new(Environment.NewLine));
            }
        }

        return spans;
    }

    private bool TryTakeBoundary(out string text)
    {
        var value = _pending.ToString();
        var newline = value.IndexOfAny(['\r', '\n']);
        if (newline >= 0)
        {
            var length = newline + 1;
            if (value[newline] == '\r' && value.Length > length && value[length] == '\n')
            {
                length++;
            }

            text = value[..length];
            _pending.Remove(0, length);
            return true;
        }

        if (value.Length < 120)
        {
            text = string.Empty;
            return false;
        }

        var boundary = value.LastIndexOfAny([' ', '\t', '.', '!', '?']);
        if (boundary < 1)
        {
            boundary = value.Length;
        }
        else
        {
            boundary++;
        }

        text = value[..boundary];
        _pending.Remove(0, boundary);
        return true;
    }

    private static string FormatBlockPrefix(string line)
    {
        if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
        {
            return $"• {line[2..]}";
        }

        return line;
    }

    private static void AddInline(ICollection<TerminalTextSpan> spans, string text)
    {
        var index = 0;
        foreach (Match match in InlinePattern.Matches(text))
        {
            if (match.Index > index)
            {
                spans.Add(new(text[index..match.Index]));
            }

            var token = match.Value;
            if (token.StartsWith("![", StringComparison.Ordinal))
            {
                spans.Add(new($"Image: {token[2..token.IndexOf(']')]}", TerminalTextStyle.Underline));
            }
            else if (token.StartsWith("[", StringComparison.Ordinal))
            {
                var bracket = token.IndexOf(']');
                spans.Add(new(token[1..bracket], TerminalTextStyle.Underline));
                spans.Add(new($" ({token[(bracket + 2)..^1]})", TerminalTextStyle.Muted));
            }
            else if (token.StartsWith("**", StringComparison.Ordinal) || token.StartsWith("__", StringComparison.Ordinal))
            {
                spans.Add(new(token[2..^2], TerminalTextStyle.Bold));
            }
            else if (token.StartsWith('`'))
            {
                spans.Add(new(token[1..^1], TerminalTextStyle.Code));
            }
            else
            {
                spans.Add(new(token[1..^1], TerminalTextStyle.Italic));
            }

            index = match.Index + match.Length;
        }

        if (index < text.Length)
        {
            spans.Add(new(text[index..]));
        }
    }
}

/// <summary>Writes incremental Markdown output through a console-independent text sink.</summary>
public sealed class ConsoleStreamWriter
{
    private readonly ITerminalConsole _console;
    private readonly IncrementalMarkdownConsoleRenderer _renderer = new();

    public ConsoleStreamWriter(ITerminalConsole console) => _console = console;

    public void WriteDelta(string delta)
    {
        foreach (var chunk in _renderer.Append(delta))
        {
            Write(chunk);
        }
    }

    public void Complete() => Write(_renderer.Flush());

    public void Write(IReadOnlyList<TerminalTextSpan> spans)
    {
        foreach (var span in spans)
        {
            _console.Write(span.Text, _console.IsOutputRedirected ? TerminalTextStyle.Plain : span.Style);
        }
    }
}
