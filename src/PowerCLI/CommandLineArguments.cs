using System.Text;

namespace PowerCLI;

public static class CommandLineArguments
{
    public static string QuoteIfNeeded(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Length > 0 && !value.Any(character => char.IsWhiteSpace(character) || character is '"' or '\\' or '\'')
            ? value : "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    public static string Unquote(string value)
    {
        var tokens = Tokenize(value.Trim(), true);
        return tokens.Count == 1 ? tokens[0].Value : value;
    }

    public static IReadOnlyList<string> Parse(string input) => Tokenize(input).Select(token => token.Value).ToArray();

    public static IReadOnlyList<CommandToken> Tokenize(string input, bool tolerant = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        var tokens = new List<CommandToken>();
        var index = 0;
        while (index < input.Length)
        {
            if (char.IsWhiteSpace(input[index])) { index++; continue; }
            var start = index;
            var text = new StringBuilder();
            var quote = '\0';
            while (index < input.Length)
            {
                var character = input[index];
                if (quote == '\0' && char.IsWhiteSpace(character)) break;
                index++;
                if (quote == '"' && character == '\\')
                {
                    if (index < input.Length) text.Append(input[index++]);
                    else if (!tolerant) throw new CommandInputException($"Incomplete escape at offset {index - 1}.", index - 1, 1);
                    continue;
                }
                if (character is '"' or '\'' && (quote == '\0' || quote == character))
                {
                    quote = quote == '\0' ? character : '\0';
                    continue;
                }
                text.Append(character);
            }
            if (quote != '\0' && !tolerant) throw new CommandInputException($"Unclosed quote at offset {start}.", start, index - start);
            tokens.Add(new(text.ToString(), start, index - start));
        }
        return tokens.AsReadOnly();
    }
}
