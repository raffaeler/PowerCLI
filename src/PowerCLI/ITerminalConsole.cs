namespace PowerCLI;

public interface ITerminalConsole
{
    bool IsInputRedirected { get; }
    bool IsOutputRedirected { get; }
    void Write(string value, TerminalTextStyle style = TerminalTextStyle.Plain);
    void WriteLine(string value = "");
    ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default);
    void Clear();
}
