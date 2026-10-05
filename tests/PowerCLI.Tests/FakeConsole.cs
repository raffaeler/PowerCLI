using System.Text;
using PowerCLI;

namespace PowerCLI.Tests;

internal sealed class FakeConsole(bool inputRedirected = false, bool outputRedirected = false) : ITerminalConsole
{
    public bool IsInputRedirected { get; } = inputRedirected;
    public bool IsOutputRedirected { get; } = outputRedirected;
    public string? Line { get; init; }
    public Queue<string?> Lines { get; } = [];
    public int LineReads { get; private set; }
    public int Clears { get; private set; }
    public StringBuilder Output { get; } = new();
    public List<TerminalTextStyle> Styles { get; } = [];
    public void Write(string value, TerminalTextStyle style = TerminalTextStyle.Plain)
    {
        Styles.Add(style);
        Output.Append(value);
    }
    public void WriteLine(string value = "") => Output.AppendLine(value);
    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        LineReads++;
        return ValueTask.FromResult(Lines.Count > 0 ? Lines.Dequeue() : Line);
    }
    public void Clear() { Clears++; Output.Clear(); }
}
