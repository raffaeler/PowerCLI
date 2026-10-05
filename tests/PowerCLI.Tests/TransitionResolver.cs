using PowerCLI;

namespace PowerCLI.Tests;

internal sealed class TransitionResolver : IOptionPickerResolver
{
    public ValueTask<IReadOnlyList<TerminalCompletion>> ResolveAsync(string input, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<TerminalCompletion>>([]);
    public ValueTask<OptionPicker?> ResolvePickerAsync(string input, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<OptionPicker?>(input.StartsWith("/load ", StringComparison.Ordinal)
            ? new("/load ", [new("Human Approval", "Human Approval")])
            : input.StartsWith('/') ? new("", [new("/load ", "/load", QuoteWhenInserted: false)]) : null);
}
