using PowerCLI;

namespace PowerCLI.Tests;

internal sealed class TestChoiceProvider : ICommandChoiceProvider
{
    internal IReadOnlyList<TerminalOption> Options { get; set; } = [new("Canonical", "Canonical")];
    internal OptionPickerMode Mode { get; set; }
    internal bool CaseSensitive { get; set; }
    internal Exception? Failure { get; set; }
    internal List<CommandChoiceContext> Contexts { get; } = [];
    public ValueTask<CommandChoiceSnapshot> GetChoicesAsync(CommandChoiceContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Failure is not null) throw Failure;
        Contexts.Add(context);
        return ValueTask.FromResult(new CommandChoiceSnapshot(Options, CaseSensitive, Mode));
    }
}
