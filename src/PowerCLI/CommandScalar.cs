using System.Globalization;

namespace PowerCLI;

public sealed record CommandScalar(string Type, string Text, long? Integer = null, double? Number = null, bool? Boolean = null)
{
    internal static CommandScalar Convert(string text, CommandValueDefinition definition)
    {
        CommandScalar value = definition.Type switch
        {
            "string" => new("string", text),
            "relativePath" when !string.IsNullOrWhiteSpace(text) && !text.StartsWith('/') && !text.StartsWith('\\') &&
                !Path.IsPathRooted(text) && !(text.Length > 1 && text[1] == ':') &&
                !text.Split(['/', '\\']).Contains("..") => new("relativePath", text),
            "integer" when long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer) =>
                new("integer", text, Integer: integer),
            "number" when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) =>
                new("number", text, Number: number),
            "boolean" when bool.TryParse(text, out var boolean) => new("boolean", text, Boolean: boolean),
            _ => throw new FormatException($"'{text}' is not a valid {definition.Type}.")
        };
        if (value.Integer is { } integral && OutsideIntegerBounds(integral, definition.Min, definition.Max) ||
            value.Number is { } numeric && ((definition.Min is { } min && numeric < min) || (definition.Max is { } max && numeric > max)))
            throw new FormatException($"'{text}' is outside the numeric bounds.");
        if ((definition.MinLength is { } minLength && text.Length < minLength) ||
            (definition.MaxLength is { } maxLength && text.Length > maxLength))
            throw new FormatException($"'{text}' is outside the string length bounds.");
        return value;
    }

    private static bool OutsideIntegerBounds(long value, double? min, double? max)
    {
        // Compare integer thresholds as integers: converting large values to double loses units.
        const double upperExclusive = 9223372036854775808d;
        return min is { } lower && (lower >= upperExclusive || lower > long.MinValue && value < (long)Math.Ceiling(lower)) ||
            max is { } upper && (upper < long.MinValue || upper < upperExclusive && value > (long)Math.Floor(upper));
    }
}
