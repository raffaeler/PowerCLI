using PowerCLI;

namespace PowerCLI.Tests;

internal sealed class RecordingDispatcher : ICommandDispatcher
{
    internal List<string?> Inputs { get; } = [];
    internal IAsyncEnumerable<TerminalOutput>? Output { get; init; }
    public ValueTask<TerminalCommandResult> HandleAsync(string? input, CancellationToken cancellationToken = default)
    {
        Inputs.Add(input);
        return ValueTask.FromResult(input switch
        {
            "/clear" => new(true, [], ClearScreen: true),
            "/output" => new(true, [], Output: Output),
            "/exit" => new(true, ["Goodbye."], ExitRequested: true),
            _ => TerminalCommandResult.NotACommand
        });
    }
}
