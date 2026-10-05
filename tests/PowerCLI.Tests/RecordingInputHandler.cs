using System.Runtime.CompilerServices;
using PowerCLI;

namespace PowerCLI.Tests;

internal sealed class RecordingInputHandler : ITerminalInputHandler
{
    internal List<string> Inputs { get; } = [];
    internal IReadOnlyList<TerminalOutput> Output { get; init; } = [new("**answer**", TerminalOutputKind.Markdown)];
    internal Exception? Failure { get; init; }

    public async IAsyncEnumerable<TerminalOutput> HandleAsync(string input,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Inputs.Add(input);
        await Task.Yield();
        if (Failure is { } failure) throw failure;
        foreach (var item in Output)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }
    }
}
