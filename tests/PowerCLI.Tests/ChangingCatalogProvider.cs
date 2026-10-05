using PowerCLI.Demo;

namespace PowerCLI.Tests;

internal sealed class ChangingCatalogProvider(TerminalCatalog first, TerminalCatalog next) : ITerminalCatalogProvider
{
    private readonly TerminalCatalog _first = first;
    private readonly TerminalCatalog _next = next;
    private int _calls;
    public ValueTask<TerminalCatalog> GetCatalogAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Interlocked.Increment(ref _calls) == 1 ? _first : _next);
}
