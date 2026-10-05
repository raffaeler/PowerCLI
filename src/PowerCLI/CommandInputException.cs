namespace PowerCLI;

internal sealed class CommandInputException(string message, int start, int length) : FormatException(message)
{
    internal int Start { get; } = start;
    internal int Length { get; } = length;
}
