using PowerCLI;
using PowerCLI.Demo;

var catalog = new TerminalCatalog(
[
    new("friendly-helper", "Friendly helper", TerminalDocumentKind.Agent, "---\nname: Friendly helper\n---\nAnswers demo questions using the selected skills.", Description: "A bogus prompt agent."),
    new("release-notes", "Release-note writer", TerminalDocumentKind.Agent, "---\nname: Release-note writer\n---\nCreates clear release notes.", Description: "A bogus prompt agent."),
    new("concise", "Be concise", TerminalDocumentKind.Skill, "Keep answers concise and action-oriented."),
    new("examples", "Include examples", TerminalDocumentKind.Skill, "Include a bogus example."),
    new("Human Approval", "Human approval", TerminalDocumentKind.Skill, "Ask before taking a bogus action."),
    new("safe-demo", "Safe demo answers", TerminalDocumentKind.Instruction, "Never expose secrets or tool arguments."),
    new("disabled-example", "Disabled example", TerminalDocumentKind.Instruction, "This must not be selectable.", IsEnabled: false)
],
[
    new("number-guesser", "Number guesser", "Pretends to guess a number.", "Start --> Guess --> Evaluate --> End"),
    new("critic-answer", "Critic-assisted answer", "Drafts and critiques a fake answer.", "Start --> Draft --> Critic --> Revise --> End"),
    new("nested-part", "Nested part", "Inspectable subworkflow.", "Child --> End", IsSubworkflow: true)
]);

var provider = new InMemoryTerminalCatalogProvider(catalog);
var selection = new TerminalSelectionSession();
var tools = new TerminalToolCatalog(
    externalTools: _ => ValueTask.FromResult<IReadOnlyList<TerminalTool>>(
    [
        new("bogus_lookup", "Returns a made-up result.", false, (request, _) =>
            ValueTask.FromResult($"Bogus result for '{request.Arguments.GetValueOrDefault("query", "nothing")}'."))
    ]));
var configuration = CommandConfiguration.FromJson(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "commands.json")));
var console = new SystemTerminalConsole();
var inputHandler = new DemoInputHandler(provider, selection, tools, console,
    new BogusAgent(), new BogusWorkflowRunner(), new InteractiveApprovalPolicy(console));
var registry = DemoCommandHost.Create(configuration, provider, selection, tools, inputHandler);
var commandHandler = new TerminalCommandHandler(registry);
var editor = new InteractiveLineEditor(console, new TerminalCompletionResolver(registry));
var service = new TerminalClientService(
    console,
    commandHandler,
    inputHandler,
    editor,
    options: new TerminalClientOptions
    {
        Prompt = "You> ",
        WelcomeMessage = "Type /help for commands."
    });

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
console.WriteLine("Type / to open commands. Up/Down navigates; Space toggles skills/instructions.");
console.WriteLine("Enter accepts a choice; Enter again submits. Esc closes the menu.");
try
{
    await service.RunAsync(cancellation.Token);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    console.WriteLine("Cancelled.");
}
