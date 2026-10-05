using PowerCLI;

namespace PowerCLI.Tests;

internal sealed class ThrowingLineEditor(Exception failure) : ILineEditor
{
    private readonly Exception _failure = failure;

    public ValueTask<string?> ReadLineAsync(string prompt, CancellationToken cancellationToken = default) =>
        throw _failure;
}
