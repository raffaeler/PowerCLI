using PowerCLI.Demo;

namespace PowerCLI.Tests;

internal sealed class RecordingAgent : ITerminalAgent
{
    internal List<string> Prompts { get; } = [];
    internal bool RequestTool { get; init; }
    public async IAsyncEnumerable<TerminalActivity> RespondAsync(string prompt, TerminalSelection selection,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Prompts.Add(prompt);
        await Task.Yield();
        if (RequestTool) yield return new(TerminalActivityKind.ToolRequested, "", "get_current_utc_time");
        yield return new(TerminalActivityKind.Answer, "**answer**");
    }
}
