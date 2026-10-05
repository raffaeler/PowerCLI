using PowerCLI;
using Xunit;

namespace PowerCLI.Tests;

public sealed class GenericTerminalTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void LibraryDoesNotExportOrReferenceSampleDomainTypes()
    {
        var assembly = typeof(TerminalClientService).Assembly;
        var names = assembly.GetExportedTypes().Select(type => type.Name).ToArray();
        foreach (var forbidden in new[] { "Agent", "Workflow", "Catalog", "Document", "Selection", "Skill", "Instruction", "Tool", "Model", "Startup" })
            Assert.DoesNotContain(names, name => name.Contains(forbidden, StringComparison.Ordinal));
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name == "PowerCLI.Demo");
        Assert.Equal(["Text", "Markdown"], Enum.GetNames<TerminalOutputKind>());
        Assert.DoesNotContain(typeof(TerminalCommandResult).GetProperties(), property => property.Name == "WorkflowToRun");
    }

    [Fact]
    public async Task CommandOnlyHostNeedsNoDomainServicesOrInputHandler()
    {
        var console = new FakeConsole(true, true);
        console.Lines.Enqueue("/help");
        console.Lines.Enqueue("unhandled");
        console.Lines.Enqueue(null);
        var registry = new CommandRegistry(new CommandConfiguration());
        await new TerminalClientService(console, new TerminalCommandHandler(registry)).RunAsync(Token);

        Assert.Equal(["/help"], registry.CommandNames);
        Assert.Contains("/help [<command>]", console.Output.ToString());
        Assert.Contains("No input handler is configured.", console.Output.ToString());
        Assert.DoesNotContain("Agent", console.Output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HostControlsPromptWelcomeAndStreamLabels()
    {
        var console = ConsoleWith("run", "/exit");
        var inputHandler = new RecordingInputHandler
        {
            Output =
            [
                new("first ", TerminalOutputKind.Markdown, "Work> "),
                new("second", TerminalOutputKind.Markdown, "Work> "),
                new("step complete"),
                new("done", TerminalOutputKind.Markdown, "Result> "),
                new("plain", Prefix: "Log> ")
            ]
        };
        var options = new TerminalClientOptions { Prompt = "Custom> ", WelcomeMessage = "Host ready." };
        await new TerminalClientService(console, new RecordingDispatcher(), inputHandler, options: options).RunAsync(Token);

        var text = console.Output.ToString();
        Assert.StartsWith("Host ready.", text, StringComparison.Ordinal);
        Assert.Contains("Custom> ", text);
        Assert.Contains("Work> first second", text);
        Assert.Equal(1, text.Split("Work> ", StringSplitOptions.None).Length - 1);
        Assert.Contains("Result> done", text);
        Assert.Contains("Log> plain", text);
        Assert.DoesNotContain("Thinking>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Assistant>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MarkdownStateDoesNotLeakBetweenHostStreams()
    {
        var console = ConsoleWith("run", "/exit");
        var inputHandler = new RecordingInputHandler
        {
            Output =
            [
                new("```text\nunfinished code\n", TerminalOutputKind.Markdown, "First> "),
                new("**normal**", TerminalOutputKind.Markdown, "Second> ")
            ]
        };
        await new TerminalClientService(console, new RecordingDispatcher(), inputHandler).RunAsync(Token);

        Assert.Contains("Second> normal", console.Output.ToString());
        Assert.DoesNotContain("**normal**", console.Output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RedirectedOutputDoesNotRequestAnyStylingFromConsole()
    {
        var console = ConsoleWith("run", "/exit");
        var inputHandler = new RecordingInputHandler
        {
            Output =
            [
                new("**text**", TerminalOutputKind.Markdown, "Styled> ", TerminalTextStyle.Bold),
                new("plain", Style: TerminalTextStyle.Italic)
            ]
        };
        await new TerminalClientService(console, new RecordingDispatcher(), inputHandler).RunAsync(Token);
        Assert.All(console.Styles, style => Assert.Equal(TerminalTextStyle.Plain, style));
    }

    [Fact]
    public async Task WelcomeCanBeSuppressedAndUnknownCommandsDoNotBecomeHostInput()
    {
        var console = ConsoleWith("/agents", "ordinary", "/exit");
        var inputHandler = new RecordingInputHandler();
        var registry = new CommandRegistry(new()
        {
            Commands = [new() { Name = "/exit", Forms = [new() { Id = "exit", Syntax = "", Handler = "exit" }] }]
        }, new Dictionary<string, ICommandHandler>
        {
            ["exit"] = new RecordingCommandHandler { Result = new(true, [], ExitRequested: true) }
        });
        await new TerminalClientService(console, new TerminalCommandHandler(registry), inputHandler,
            options: new() { WelcomeMessage = null }).RunAsync(Token);

        Assert.Equal(["ordinary"], inputHandler.Inputs);
        Assert.Contains("Unknown command '/agents'", console.Output.ToString());
        Assert.DoesNotContain("Type /help for commands.", console.Output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HostStreamErrorsAreVisibleAndLoopContinues()
    {
        var console = ConsoleWith("fail", "/exit");
        var inputHandler = new RecordingInputHandler { Failure = new InvalidOperationException("host failure") };
        await new TerminalClientService(console, new RecordingDispatcher(), inputHandler).RunAsync(Token);

        Assert.Contains("Output failed: host failure", console.Output.ToString());
        Assert.Contains("Goodbye.", console.Output.ToString());
    }

    [Fact]
    public async Task SynchronousHostErrorsAreVisibleAndLoopContinues()
    {
        var console = ConsoleWith("fail", "/exit");
        await new TerminalClientService(console, new RecordingDispatcher(), new ThrowingInputHandler()).RunAsync(Token);

        Assert.Contains("Input failed: synchronous host failure", console.Output.ToString());
        Assert.Contains("Goodbye.", console.Output.ToString());
    }

    [Fact]
    public async Task CommandStreamErrorsAreVisibleAndLoopContinues()
    {
        var console = ConsoleWith("/output", "/exit");
        var output = new RecordingInputHandler { Failure = new InvalidOperationException("command stream failure") };
        var dispatcher = new RecordingDispatcher { Output = output.HandleAsync("sample", Token) };
        await new TerminalClientService(console, dispatcher).RunAsync(Token);

        Assert.Contains("Output failed: command stream failure", console.Output.ToString());
        Assert.Contains("Goodbye.", console.Output.ToString());
    }

    [Fact]
    public async Task OutputCancellationPropagates()
    {
        var console = ConsoleWith("cancel");
        var inputHandler = new RecordingInputHandler { Failure = new OperationCanceledException(Token) };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new TerminalClientService(console, new RecordingDispatcher(), inputHandler).RunAsync(Token));
    }

    [Fact]
    public async Task InvalidOutputKindIsReportedInsteadOfSilentlyRendered()
    {
        var console = ConsoleWith("invalid", "/exit");
        var inputHandler = new RecordingInputHandler { Output = [new("ignored", (TerminalOutputKind)99)] };
        await new TerminalClientService(console, new RecordingDispatcher(), inputHandler).RunAsync(Token);

        Assert.Contains("Unsupported terminal output kind", console.Output.ToString());
        Assert.DoesNotContain("ignored", console.Output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreCancelledRunDoesNotWriteOrRead()
    {
        var console = new FakeConsole(true, true);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new TerminalClientService(console, new RecordingDispatcher()).RunAsync(cancellation.Token));

        Assert.Empty(console.Output.ToString());
        Assert.Equal(0, console.LineReads);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClearEffectIsOnlyAppliedToInteractiveOutput(bool redirected)
    {
        var console = new FakeConsole(true, redirected);
        console.Lines.Enqueue("/clear");
        console.Lines.Enqueue("/exit");
        await new TerminalClientService(console, new RecordingDispatcher()).RunAsync(Token);
        Assert.Equal(redirected ? 0 : 1, console.Clears);
    }

    private static FakeConsole ConsoleWith(params string[] lines)
    {
        var console = new FakeConsole(true, true);
        foreach (var line in lines) console.Lines.Enqueue(line);
        return console;
    }
}
