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
        var dispatcher = new RecordingDispatcher { Output = output.HandleAsync("sample", Token) };
        var inputHandler = new RecordingInputHandler();
        await new TerminalClientService(console, dispatcher, inputHandler).RunAsync(Token);

        Assert.Equal(["/clear", "/output", "prompt", "/exit"], dispatcher.Inputs);
        Assert.Equal(["prompt"], inputHandler.Inputs);
        Assert.Equal(["sample"], output.Inputs);
        Assert.Contains("command output", console.Output.ToString());
        Assert.Equal(0, console.Clears);
        Assert.Contains("Goodbye.", console.Output.ToString());
        Assert.DoesNotContain('\u001b', console.Output.ToString());
    }

    [Fact]
    public async Task DemoRegistersOnlyMinimalCommandsWithDefaultsAliasesAndRepeatedOptions()
    {
        var registry = new DemoApplication().CreateCommands();
        Assert.Equal(["/help", "/echo", "/choose", "/export", "/clear", "/exit"], registry.CommandNames);
        var dispatcher = new TerminalCommandHandler(registry);

        Assert.Equal(["HELLO WORLD"], (await dispatcher.HandleAsync("/ECHO \"hello world\" --UPPER", Token)).Messages);
        Assert.Contains("format=text, force=False, tags=.", Assert.Single(
            (await dispatcher.HandleAsync("/export sample.txt", Token)).Messages));
        Assert.Contains("format=json, force=True, tags=one, two.", Assert.Single(
            (await dispatcher.HandleAsync("/export sample.json -f JSON -y --tag one --tag two", Token)).Messages));
        Assert.True((await dispatcher.HandleAsync("/clear", Token)).ClearScreen);
        Assert.True((await dispatcher.HandleAsync("/exit", Token)).ExitRequested);
    }

    [Fact]
    public async Task DemoChoicesReflectCurrentSelectionAndCanBeCleared()
    {
        var app = new DemoApplication();
        var registry = app.CreateCommands();
        var dispatcher = new TerminalCommandHandler(registry);
        var resolver = new TerminalCompletionResolver(registry);

        Assert.Null((await dispatcher.HandleAsync("/choose \"with examples\" concise", Token)).Diagnostics);
        var picker = await resolver.ResolvePickerAsync("/choose ", Token);
        Assert.NotNull(picker);
        Assert.Equal(OptionPickerMode.Multiple, picker.Mode);
        Assert.Equal(["concise", "with examples"], picker.Options.Where(option => option.IsSelected).Select(option => option.Value));
        Assert.Null((await dispatcher.HandleAsync("/choose", Token)).Diagnostics);
        Assert.Empty(app.SelectedItems);
        Assert.All((await resolver.ResolvePickerAsync("/choose ", Token))!.Options, option => Assert.False(option.IsSelected));
    }

    [Theory]
    [InlineData("/choose unavailable")]
    [InlineData("/export ..\\outside.txt")]
    [InlineData("/export sample.txt --format xml")]
    [InlineData("/echo text --unknown")]
    public async Task DemoInvalidInputsProduceDiagnostics(string input)
    {
        var app = new DemoApplication();
        var result = await new TerminalCommandHandler(app.CreateCommands()).HandleAsync(input, Token);

        Assert.NotNull(result.Diagnostics);
        Assert.Empty(app.SelectedItems);
        Assert.NotEmpty(result.Messages);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task DemoRedirectedLoopUsesPlainLineInputAndStreamsOrdinaryText(bool inputRedirected, bool outputRedirected)
    {
        var console = new FakeConsole(inputRedirected, outputRedirected);
        foreach (var line in new[] { "/help", "/echo \"hello world\"", "/choose friendly", "ordinary", "/exit" })
            console.Lines.Enqueue(line);
        var surface = new FakeSurface([]);
        var app = new DemoApplication();
        var registry = app.CreateCommands();
        var editor = new InteractiveLineEditor(console, new TerminalCompletionResolver(registry), surface);

        await new TerminalClientService(console, new TerminalCommandHandler(registry), app, editor).RunAsync(Token);

        Assert.Contains("hello world", console.Output.ToString());
        Assert.Contains("Demo> Sample response: ordinary", console.Output.ToString());
        Assert.Contains("Selected: friendly", console.Output.ToString());
        Assert.Contains("Goodbye.", console.Output.ToString());
        Assert.Empty(surface.Output.ToString());
        Assert.DoesNotContain('\u001b', console.Output.ToString());
        if (outputRedirected) Assert.All(console.Styles, style => Assert.Equal(TerminalTextStyle.Plain, style));
    }

    [Fact]
    public async Task DemoOrdinaryInputHonorsCancellation()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var app = new DemoApplication();
        await using var stream = app.HandleAsync("sample", cancellation.Token).GetAsyncEnumerator(Token);
        Assert.True(await stream.MoveNextAsync());
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream.MoveNextAsync().AsTask());
    }
}
