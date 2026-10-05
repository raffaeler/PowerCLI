using PowerCLI;

namespace PowerCLI.Tests;

internal sealed class ThrowingInputHandler : ITerminalInputHandler
{
    public IAsyncEnumerable<TerminalOutput> HandleAsync(string input, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("synchronous host failure");
}
