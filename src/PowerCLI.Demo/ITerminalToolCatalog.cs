namespace PowerCLI.Demo;

/// <summary>Exposes built-in and host-provided demo tools.</summary>
public interface ITerminalToolCatalog
{
    ValueTask<IReadOnlyList<TerminalTool>> GetToolsAsync(CancellationToken cancellationToken = default);
}
