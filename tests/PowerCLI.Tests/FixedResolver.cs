using PowerCLI;

namespace PowerCLI.Tests;

internal sealed class FixedResolver(OptionPicker? picker) : IOptionPickerResolver
{
    private readonly OptionPicker? _picker = picker;
    public ValueTask<IReadOnlyList<TerminalCompletion>> ResolveAsync(string input, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<TerminalCompletion>>([]);
    public ValueTask<OptionPicker?> ResolvePickerAsync(string input, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_picker);
}
