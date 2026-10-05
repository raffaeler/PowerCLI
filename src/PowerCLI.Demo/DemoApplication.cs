using System.Runtime.CompilerServices;
using PowerCLI;

namespace PowerCLI.Demo;

public sealed class DemoApplication : ITerminalInputHandler
{
    private readonly string[] _availableItems = ["concise", "with examples", "friendly"];
    private IReadOnlyList<string> _selectedItems = [];

    public IReadOnlyList<string> SelectedItems => _selectedItems;

    public CommandRegistry CreateCommands() => new CommandRegistryBuilder()
        .Command("/echo", command => command
            .Description("Echo quoted text, optionally in uppercase.")
            .Form("<text>", form => form
                .Argument("text").Flag("--upper")
                .Example("/echo \"hello world\" --upper")
                .Handle(input => TerminalCommandResult.Message(
                    input["--upper"].Boolean ? input["text"].String.ToUpperInvariant() : input["text"].String))))
        .Command("/choose", command => command
            .Description("Select sample items. Space toggles choices; an empty set clears them.")
            .Form("<items>*", form => form
                .Argument("items", value => value.Choices(GetChoices).Multiple())
                .Handle(Choose)))
        .Command("/export", command => command
            .Description("Describe a bogus export without accessing files.")
            .Form("<target>", form => form
                .Argument("target", value => value.Type("relativePath"))
                .Option("--format", "format", option => option.Alias("-f").Choices("text", "json").Default("text"))
                .Flag("--force", option => option.Alias("-y"))
                .Option("--tag", "tags", option => option.Repeat())
                .Example("/export sample.json -f json --tag bogus")
                .Handle(input => TerminalCommandResult.Message(
                    $"Bogus export to '{input["target"].String}', format={input["--format"].String}, " +
                    $"force={input["--force"].Boolean}, tags={string.Join(", ", input["--tag"].Strings)}."))))
        .Command("/clear", command => command
            .Description("Clear the screen.")
            .Form("", form => form.Handle(_ => new(true, [], ClearScreen: true))))
        .Command("/exit", command => command
            .Description("Exit the demo.")
            .Form("", form => form.Handle(_ => new(true, ["Goodbye."], ExitRequested: true))))
        .Build();

    public async IAsyncEnumerable<TerminalOutput> HandleAsync(string input,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new("**Sample response:** ", TerminalOutputKind.Markdown, "Demo> ");
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        yield return new(input, TerminalOutputKind.Markdown, "Demo> ");
        yield return new($"Selected: {(_selectedItems.Count == 0 ? "(none)" : string.Join(", ", _selectedItems))}");
    }

    private ValueTask<CommandChoiceSnapshot> GetChoices(CommandChoiceContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new CommandChoiceSnapshot(_availableItems
            .Select(item => new TerminalOption(item, item, _selectedItems.Contains(item, StringComparer.OrdinalIgnoreCase)))
            .ToArray(), Mode: OptionPickerMode.Multiple));
    }

    private TerminalCommandResult Choose(CommandInvocation input)
    {
        _selectedItems = Array.AsReadOnly(input["items"].Strings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        return TerminalCommandResult.Message(_selectedItems.Count == 0 ? "Cleared choices." :
            "Selected: " + string.Join(", ", _selectedItems));
    }
}
