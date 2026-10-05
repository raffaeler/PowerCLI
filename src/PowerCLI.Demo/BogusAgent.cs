using PowerCLI;

namespace PowerCLI.Demo;

internal sealed class BogusAgent : ITerminalAgent
{
    public async IAsyncEnumerable<TerminalActivity> RespondAsync(string prompt, TerminalSelection selection,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new(TerminalActivityKind.Reasoning, "I am considering the demo selections. ");
        await Task.Delay(40, cancellationToken);
        if (prompt.Contains("time", StringComparison.OrdinalIgnoreCase))
            yield return new(TerminalActivityKind.ToolRequested, "", "get_current_utc_time");
        yield return new(TerminalActivityKind.Answer, $"**Bogus answer:** you said _{prompt}_. ");
        yield return new(TerminalActivityKind.Answer, $"Agent: `{selection.AgentId ?? "default"}`; target: `{selection.TargetPath}`.");
    }
}
