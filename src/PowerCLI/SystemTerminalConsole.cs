namespace PowerCLI;

public sealed class SystemTerminalConsole : ITerminalConsole
{
    public bool IsInputRedirected => Console.IsInputRedirected;
    public bool IsOutputRedirected => Console.IsOutputRedirected;
    public void Write(string value, TerminalTextStyle style = TerminalTextStyle.Plain)
    {
        if (IsOutputRedirected || style == TerminalTextStyle.Plain) { Console.Write(value); return; }
        Console.Write(Ansi(style));
        Console.Write(value);
        Console.Write("\u001b[0m");
    }
    public void WriteLine(string value = "") => Console.WriteLine(value);
    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default) => await Console.In.ReadLineAsync(cancellationToken);
    public void Clear() { if (!IsOutputRedirected) Console.Clear(); }
    private static string Ansi(TerminalTextStyle style) => style switch
    {
        TerminalTextStyle.Bold => "\u001b[1m",
        TerminalTextStyle.Italic => "\u001b[3m",
        TerminalTextStyle.Underline => "\u001b[4m",
        TerminalTextStyle.Heading => "\u001b[1;36m",
        TerminalTextStyle.Code => "\u001b[38;5;214m",
        TerminalTextStyle.Muted => "\u001b[2m",
        _ => ""
    };
}
