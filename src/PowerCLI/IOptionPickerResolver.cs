namespace PowerCLI;

public interface IOptionPickerResolver
{
    ValueTask<IReadOnlyList<TerminalCompletion>> ResolveAsync(string input, CancellationToken cancellationToken = default);

    async ValueTask<OptionPicker?> ResolvePickerAsync(string input, CancellationToken cancellationToken = default)
    {
        var options = await ResolveAsync(input, cancellationToken);
        if (options.Count == 0) return null;
        var start = input.LastIndexOf(' ') + 1;
        return new(input[..start], options.Select(option => new TerminalOption(option.Value, option.Label, option.IsSelected)).ToArray());
    }

    ValueTask<OptionPicker?> ResolvePickerAsync(CompletionRequest request, CancellationToken cancellationToken = default) =>
        ResolvePickerAsync(request.Input, cancellationToken);
}
