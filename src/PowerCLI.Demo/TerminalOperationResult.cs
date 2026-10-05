namespace PowerCLI.Demo;

/// <summary>The outcome of a selection operation.</summary>
public sealed record TerminalOperationResult(bool Succeeded, string Message)
{
    public static TerminalOperationResult Success(string message) => new(true, message);
    public static TerminalOperationResult Failure(string message) => new(false, message);
}
