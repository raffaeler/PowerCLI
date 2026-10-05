namespace PowerCLI.Demo;

/// <summary>A catalog containing the UTC time tool plus optional externally supplied tools.</summary>
public sealed class TerminalToolCatalog : ITerminalToolCatalog
{
    private readonly TimeProvider _timeProvider;
    private readonly Func<CancellationToken, ValueTask<IReadOnlyList<TerminalTool>>> _externalTools;

    public TerminalToolCatalog(
        TimeProvider? timeProvider = null,
        Func<CancellationToken, ValueTask<IReadOnlyList<TerminalTool>>>? externalTools = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _externalTools = externalTools ?? (_ => ValueTask.FromResult<IReadOnlyList<TerminalTool>>([]));
    }

    public async ValueTask<IReadOnlyList<TerminalTool>> GetToolsAsync(CancellationToken cancellationToken = default)
    {
        var tools = new List<TerminalTool>
        {
            new("get_current_utc_time", "Gets the current UTC time.", true, (_, _) =>
                ValueTask.FromResult(_timeProvider.GetUtcNow().ToString("O")))
        };
        tools.AddRange(await _externalTools(cancellationToken));
        return tools;
    }
}
