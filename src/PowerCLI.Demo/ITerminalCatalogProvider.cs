namespace PowerCLI.Demo;

/// <summary>Supplies catalog snapshots. Implementations may refresh remote or file-backed catalogs.</summary>
public interface ITerminalCatalogProvider
{
    ValueTask<TerminalCatalog> GetCatalogAsync(CancellationToken cancellationToken = default);
}
