using PowerCLI;
using PowerCLI.Demo;
using Xunit;

namespace PowerCLI.Tests;

public sealed class CommandHostTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AsynchronousChoiceLookupCancellationPropagates(bool completion)
    {
        var provider = new BlockingChoiceProvider();
        var form = ConfigurableCommandTests.Form("<a>", "a") with
        { Arguments = new Dictionary<string, CommandValueDefinition> { ["a"] = new() { Choices = new() { Provider = "p" } } } };
        var handler = new RecordingCommandHandler();
        var registry = new CommandRegistry(new() { Commands = [new() { Name = "/x", Forms = [form] }] },
            new Dictionary<string, ICommandHandler> { ["h"] = handler }, new Dictionary<string, ICommandChoiceProvider> { ["p"] = provider });
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        Task task = completion
            ? new TerminalCompletionResolver(registry).ResolvePickerAsync("/x ", cancellation.Token).AsTask()
            : new TerminalCommandHandler(registry).HandleAsync("/x value", cancellation.Token).AsTask();
        await provider.Started.Task.WaitAsync(Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Empty(handler.Invocations);
    }

    [Fact]
    public async Task ServiceUsesInjectedDispatcherPreservesEffectsAndRoutesNonCommandsToHost()
    {
        var console = new FakeConsole(true, true);
        foreach (var line in new[] { "/clear", "/output", "prompt", "/exit" }) console.Lines.Enqueue(line);
        var output = new RecordingInputHandler { Output = [new("command output")] };
        var dispatcher = new RecordingDispatcher { Output = output.HandleAsync("command", Token) };
        var inputHandler = new RecordingInputHandler();
        var service = new TerminalClientService(console, dispatcher, inputHandler);
        await service.RunAsync(Token);
        Assert.Equal(["/clear", "/output", "prompt", "/exit"], dispatcher.Inputs);
        Assert.Equal(["prompt"], inputHandler.Inputs);
        Assert.Equal(["command"], output.Inputs);
        Assert.Contains("command output", console.Output.ToString());
        Assert.Equal(0, console.Clears);
        Assert.Contains("Goodbye.", console.Output.ToString());
        Assert.DoesNotContain("\u001b", console.Output.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task DemoRedirectsNeverInvokeApprovalPolicy(bool inputRedirected, bool outputRedirected)
    {
        var console = new FakeConsole(inputRedirected, outputRedirected);
        console.Lines.Enqueue("time");
        console.Lines.Enqueue("/exit");
        var approval = new RecordingApprovalPolicy();
        var inputHandler = new DemoInputHandler(new InMemoryTerminalCatalogProvider(TerminalCatalog.Empty), new(),
            new TerminalToolCatalog(), console: console, agent: new RecordingAgent { RequestTool = true }, approvalPolicy: approval);
        await new TerminalClientService(console, new RecordingDispatcher(), inputHandler).RunAsync(Token);
        Assert.Equal(0, approval.Calls);
        Assert.Contains("was denied", console.Output.ToString());
    }

    [Fact]
    public async Task DemoOwnsSelectionBasedRoutingBehindGenericInputInterface()
    {
        var selection = new TerminalSelectionSession();
        Assert.True(selection.SelectWorkflow(Catalog(), "safe-workflow").Succeeded);
        var console = new FakeConsole(true, true);
        console.Lines.Enqueue("prompt");
        console.Lines.Enqueue("/exit");
        var agent = new RecordingAgent();
        var runner = new RecordingWorkflowRunner();
        ITerminalInputHandler inputHandler = new DemoInputHandler(new InMemoryTerminalCatalogProvider(Catalog()), selection,
            new TerminalToolCatalog(), console: console, agent: agent, workflowRunner: runner, approvalPolicy: new RecordingApprovalPolicy());

        await new TerminalClientService(console, new RecordingDispatcher(), inputHandler).RunAsync(Token);

        Assert.Empty(agent.Prompts);
        Assert.Equal(["safe-workflow"], runner.Workflows);
        Assert.Contains("workflow complete", console.Output.ToString());
    }

    [Fact]
    public async Task DemoToolApprovalRemainsHostControlledForInteractiveConsole()
    {
        var console = new FakeConsole();
        var approval = new RecordingApprovalPolicy();
        var inputHandler = new DemoInputHandler(new InMemoryTerminalCatalogProvider(TerminalCatalog.Empty), new(),
            new TerminalToolCatalog(new FakeTimeProvider()), console: console,
            agent: new RecordingAgent { RequestTool = true }, approvalPolicy: approval);
        var output = new List<string>();
        await foreach (var item in inputHandler.HandleAsync("time", Token)) output.Add((item.Prefix ?? "") + item.Content);

        Assert.Equal(1, approval.Calls);
        Assert.Contains(output, text => text.Contains("completed", StringComparison.Ordinal) &&
            text.Contains("2026-10-05", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DemoExplicitlyRegistersAllApplicationCommandsAndGenericExport()
    {
        var registry = DemoTestCommands.Create(new InMemoryTerminalCatalogProvider(TerminalCatalog.Empty), new());
        Assert.Equal(14, registry.CommandNames.Count);
        Assert.Equal(["/help", "/agents", "/workflows", "/skills", "/instructions", "/tools", "/workflow", "/use",
            "/print", "/session", "/new", "/clear", "/exit", "/export"], registry.CommandNames);
        var dispatcher = new TerminalCommandHandler(registry);
        Assert.True((await dispatcher.HandleAsync("/clear", Token)).ClearScreen);
        Assert.True((await dispatcher.HandleAsync("/exit", Token)).ExitRequested);
        Assert.Contains("format=json", Assert.Single((await dispatcher.HandleAsync("/export sample.json -f JSON --tag bogus", Token)).Messages));
    }

    [Theory]
    [InlineData("/use agent unsafe-agent")]
    [InlineData("/use workflow unsafe-workflow")]
    [InlineData("/use agent nested")]
    [InlineData("/workflow no-prompt")]
    [InlineData("/use instruction disabled")]
    [InlineData("/use skill unsafe-skill")]
    [InlineData("/print instruction disabled")]
    [InlineData("/print agent unsafe-agent")]
    [InlineData("/print workflow unsafe-workflow")]
    public async Task DemoNeverSelectsOrPrintsUntrustedDisabledOrRestrictedResources(string input)
    {
        var selection = new TerminalSelectionSession();
        var registry = DemoTestCommands.Create(new InMemoryTerminalCatalogProvider(Catalog()), selection);
        var result = await new TerminalCommandHandler(registry).HandleAsync(input, Token);
        Assert.NotNull(result.Diagnostics);
        Assert.Equal(TerminalSelection.Empty, selection.Current);
        Assert.DoesNotContain("SECRET BODY", string.Join('\n', result.Messages), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/use agent ")]
    [InlineData("/use workflow ")]
    [InlineData("/use skill ")]
    [InlineData("/use instruction ")]
    [InlineData("/print instruction ")]
    [InlineData("/print workflow ")]
    [InlineData("/workflow ")]
    public async Task DemoCompletionFiltersTrustAndSelectableKinds(string input)
    {
        var registry = DemoTestCommands.Create(new InMemoryTerminalCatalogProvider(Catalog()), new());
        var options = await new TerminalCompletionResolver(registry).ResolveAsync(input, Token);
        Assert.DoesNotContain(options, option => option.Value.StartsWith("unsafe", StringComparison.Ordinal) || option.Value == "disabled");
        if (!input.StartsWith("/print", StringComparison.Ordinal))
            Assert.DoesNotContain(options, option => option.Value is "nested" or "no-prompt");
        Assert.DoesNotContain(options, option => option.Value == "workflow-document");
    }

    [Theory]
    [InlineData("/use agent safe-agent")]
    [InlineData("/use skill safe-skill")]
    [InlineData("/use instruction safe-instruction")]
    [InlineData("/print agent safe-agent")]
    [InlineData("/workflow safe-workflow")]
    public async Task DemoHandlersRevalidateTrustAfterDispatchSnapshot(string input)
    {
        var trusted = new TerminalCatalog([
            new("safe-agent", "Agent", TerminalDocumentKind.Agent, "SECRET BODY"),
            new("safe-skill", "Skill", TerminalDocumentKind.Skill, "SECRET BODY"),
            new("safe-instruction", "Instruction", TerminalDocumentKind.Instruction, "SECRET BODY")
        ], [new("safe-workflow", "Workflow", "", "")]);
        var changed = new TerminalCatalog(trusted.Documents.Select(document => document with { IsTrusted = false }),
            trusted.Workflows.Select(workflow => workflow with { IsTrusted = false }));
        var selection = new TerminalSelectionSession();
        var registry = DemoTestCommands.Create(new ChangingCatalogProvider(trusted, changed), selection);
        var result = await new TerminalCommandHandler(registry).HandleAsync(input, Token);
        Assert.Equal(TerminalSelection.Empty, selection.Current);
        Assert.Null(result.Output);
        Assert.DoesNotContain("SECRET BODY", string.Join('\n', result.Messages), StringComparison.Ordinal);
        Assert.Contains(result.Messages, message => message.Contains("untrusted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DemoSupportsSelectionResetPrintingAndWorkflowEffects()
    {
        var catalog = Catalog();
        var selection = new TerminalSelectionSession();
        var dispatcher = new TerminalCommandHandler(DemoTestCommands.Create(new InMemoryTerminalCatalogProvider(catalog), selection));
        await dispatcher.HandleAsync("/use skill safe-skill", Token);
        Assert.Equal(["safe-skill"], selection.Current.SkillIds);
        await dispatcher.HandleAsync("/use instruction safe-instruction", Token);
        Assert.Equal(["safe-instruction"], selection.Current.InstructionIds);
        await dispatcher.HandleAsync("/use agent safe-agent", Token);
        Assert.Equal("safe-agent", selection.Current.AgentId);
        await dispatcher.HandleAsync("/use target sample.md", Token);
        Assert.Equal("sample.md", selection.Current.TargetPath);
        var output = (await dispatcher.HandleAsync("/workflow safe-workflow", Token)).Output;
        Assert.NotNull(output);
        var fragments = new List<string>();
        await foreach (var fragment in output.WithCancellation(Token)) fragments.Add(fragment.Content);
        Assert.Contains("safe-workflow", string.Join('\n', fragments), StringComparison.Ordinal);
        Assert.Equal("safe body", Assert.Single((await dispatcher.HandleAsync("/print skill safe-skill", Token)).Messages));
        await dispatcher.HandleAsync("/new", Token);
        Assert.Equal(TerminalSelection.Empty, selection.Current);
    }

    private static TerminalCatalog Catalog() => new([
        new("safe-agent", "Agent", TerminalDocumentKind.Agent, "safe body"),
        new("safe-skill", "Skill", TerminalDocumentKind.Skill, "safe body"),
        new("safe-instruction", "Instruction", TerminalDocumentKind.Instruction, "safe body"),
        new("unsafe-agent", "Agent", TerminalDocumentKind.Agent, "SECRET BODY", IsTrusted: false),
        new("unsafe-skill", "Skill", TerminalDocumentKind.Skill, "SECRET BODY", IsTrusted: false),
        new("disabled", "Disabled", TerminalDocumentKind.Instruction, "SECRET BODY", IsEnabled: false),
        new("workflow-document", "Not a skill", TerminalDocumentKind.Workflow, "SECRET BODY")
    ], [
        new("safe-workflow", "Safe workflow", "", ""),
        new("unsafe-workflow", "Unsafe workflow", "SECRET BODY", "SECRET BODY", IsTrusted: false),
        new("nested", "Nested", "", "", IsSubworkflow: true),
        new("no-prompt", "No prompt", "", "", AcceptsPrompt: false)
    ]);
}
