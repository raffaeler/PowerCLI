namespace PowerCLI.Demo;

/// <summary>A simple thread-safe catalog provider for hosts, tests, and sample applications.</summary>
public sealed class InMemoryTerminalCatalogProvider : ITerminalCatalogProvider
{
    private TerminalCatalog _catalog;

    public InMemoryTerminalCatalogProvider(TerminalCatalog catalog) => _catalog = catalog;

    public ValueTask<TerminalCatalog> GetCatalogAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Volatile.Read(ref _catalog));

    public void Replace(TerminalCatalog catalog) =>
        Interlocked.Exchange(ref _catalog, catalog ?? throw new ArgumentNullException(nameof(catalog)));
}
