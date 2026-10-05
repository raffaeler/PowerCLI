using PowerCLI;
using PowerCLI.Demo;
using Xunit;

namespace PowerCLI.Tests;

public sealed class InteractiveLineEditorTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FiltersNavigatesAndAcceptsBeforeSubmitting()
    {
        var surface = new FakeSurface([Character('b'), Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter), Key(ConsoleKey.Enter)]);
        var editor = CreateEditor(surface, new FixedResolver(new("", [new("alpha", "Alpha"), new("beta", "Beta"), new("bravo", "Bravo")])));

        Assert.Equal("bravo", await editor.ReadLineAsync("You> ", Token));
        Assert.Contains("Bravo", surface.Output.ToString());
        Assert.Contains("Bravo", surface.Highlighted);
        Assert.Equal(0, surface.RemainingKeys);
    }

    [Fact]
    public async Task ExactChoiceSubmitsOnFirstEnter()
    {
        var surface = new FakeSurface([.. Text("/help"), Key(ConsoleKey.Enter)]);
        var editor = CreateEditor(surface, new FixedResolver(new("", [new("/help", "/help")])));

        Assert.Equal("/help", await editor.ReadLineAsync("You> ", Token));
        Assert.Equal(0, surface.RemainingKeys);
    }

    [Fact]
    public async Task CommandSelectionChainsIntoArgumentMenuAndQuotesValue()
    {
        var surface = new FakeSurface([Character('/'), Key(ConsoleKey.Enter), Key(ConsoleKey.Enter), Key(ConsoleKey.Enter)]);
        var editor = CreateEditor(surface, new TransitionResolver());

        Assert.Equal("/load \"Human Approval\"", await editor.ReadLineAsync("You> ", Token));
        Assert.Contains("Human Approval", surface.Output.ToString());
    }

    [Fact]
    public async Task SpaceTogglesMultipleOptionsWithoutInsertingNumbers()
    {
        var surface = new FakeSurface(
        [
            .. Text("/use skill "),
            Key(ConsoleKey.Spacebar), Key(ConsoleKey.DownArrow), Key(ConsoleKey.Spacebar),
            Key(ConsoleKey.Enter), Key(ConsoleKey.Enter)
        ]);
        var editor = CreateEditor(surface, new FixedResolver(new("/use skill ", [new("one", "one"), new("two", "two")], OptionPickerMode.Multiple)));

        Assert.Equal("/use skill one two", await editor.ReadLineAsync("You> ", Token));
        Assert.Contains("[X]", surface.Output.ToString());
    }

    [Fact]
    public async Task ExistingSelectionsArePreservedAndCanBeToggledOff()
    {
        var surface = new FakeSurface(
        [
            .. Text("/use skill "), Key(ConsoleKey.DownArrow), Key(ConsoleKey.Spacebar),
            Key(ConsoleKey.Enter), Key(ConsoleKey.Enter)
        ]);
        var editor = CreateEditor(surface, new FixedResolver(new("/use skill ",
            [new("one", "one", true), new("two", "two", true)], OptionPickerMode.Multiple)));

        Assert.Equal("/use skill one", await editor.ReadLineAsync("You> ", Token));
    }

    [Fact]
    public async Task EmptyMultipleSelectionCanBeAccepted()
    {
        var surface = new FakeSurface([.. Text("/use skill "), Key(ConsoleKey.Enter), Key(ConsoleKey.Enter)]);
        var editor = CreateEditor(surface, new FixedResolver(new("/use skill ", [new("one", "one")], OptionPickerMode.Multiple)));

        Assert.Equal("/use skill ", await editor.ReadLineAsync("You> ", Token));
    }

    [Fact]
    public async Task EscapeClosesMenuWithoutChangingInput()
    {
        var surface = new FakeSurface([Character('x'), Key(ConsoleKey.Escape), Key(ConsoleKey.Enter)]);
        var editor = CreateEditor(surface, new FixedResolver(new("", [new("xyz", "xyz")])));

        Assert.Equal("x", await editor.ReadLineAsync("You> ", Token));
    }

    [Fact]
    public async Task LeftRightHomeEndBackspaceAndDeleteEditAtCursor()
    {
        var surface = new FakeSurface(
        [
            .. Text("ac"), Key(ConsoleKey.LeftArrow), Character('b'),
            Key(ConsoleKey.Home), Key(ConsoleKey.Delete), Character('A'),
            Key(ConsoleKey.End), Key(ConsoleKey.Backspace), Character('C'),
            Key(ConsoleKey.LeftArrow), Key(ConsoleKey.RightArrow), Character('!'), Key(ConsoleKey.Enter)
        ]);
        var editor = CreateEditor(surface);

        Assert.Equal("AbC!", await editor.ReadLineAsync("You> ", Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LongMenusScrollAndWrapWithEllipsis(bool virtualCursor)
    {
        var surface = new FakeSurface([Character('/'), Key(ConsoleKey.UpArrow), Key(ConsoleKey.Enter), Key(ConsoleKey.Enter)],
            virtualCursor: virtualCursor);
        var options = Enumerable.Range(1, 20).Select(index => new TerminalOption($"/item{index}", $"Item {index}")).ToArray();
        var editor = CreateEditor(surface, new FixedResolver(new("", options)));

        Assert.Equal("/item20", await editor.ReadLineAsync("You> ", Token));
        Assert.Contains("...", surface.Output.ToString());
        Assert.Contains("Item 20", surface.Highlighted);
    }

    [Fact]
    public async Task FixedBufferAtBottomReservesRoomForWholeMenu()
    {
        var surface = new FakeSurface([Character('/'), Key(ConsoleKey.Escape), Key(ConsoleKey.Enter)],
            bufferHeight: 7, canExpand: false, initialTop: 6);
        var editor = CreateEditor(surface, new FixedResolver(new("", Enumerable.Range(1, 7)
            .Select(index => new TerminalOption($"/item{index}", $"Item {index}")).ToArray())));

        Assert.Equal("/", await editor.ReadLineAsync("You> ", Token));
        Assert.Contains("Item 5", surface.Output.ToString());
        Assert.Equal(1, surface.CursorTop);
    }

    [Fact]
    public async Task SmallBufferExpandsForPicker()
    {
        var surface = new FakeSurface([Character('/'), Key(ConsoleKey.Escape), Key(ConsoleKey.Enter)], bufferHeight: 1);
        var editor = CreateEditor(surface, new FixedResolver(new("", [new("/help", "/help")])));

        Assert.Equal("/", await editor.ReadLineAsync("You> ", Token));
        Assert.True(surface.BufferHeight >= 18);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RedirectedConsoleUsesLineInputWithoutKeysOrCursorMovement(bool inputRedirected, bool outputRedirected)
    {
        var console = new FakeConsole(inputRedirected, outputRedirected) { Line = "piped" };
        var surface = new FakeSurface([]);
        var editor = new InteractiveLineEditor(console, new FixedResolver(new("", [new("piped", "piped")])), surface);

        Assert.Equal("piped", await editor.ReadLineAsync("You> ", Token));
        Assert.Equal("You> ", console.Output.ToString());
        Assert.Empty(surface.Output.ToString());
    }

    [Fact]
    public async Task CancelsWhileWaitingForRawKey()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var surface = new BlockingSurface();
        var editor = new InteractiveLineEditor(new FakeConsole(), surface: surface);
        var read = editor.ReadLineAsync("You> ", source.Token).AsTask();
        await surface.ReadStarted.Task.WaitAsync(Token);
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
    }

    [Fact]
    public async Task StandalonePickerUsesArrowsAndAcceptsOnEnter()
    {
        var surface = new FakeSurface([Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter)]);
        var editor = CreateEditor(surface);

        var picked = await editor.PickAsync("Choose", [new("one", "First"), new("two", "Second")], cancellationToken: Token);

        Assert.Equal("two", Assert.Single(picked).Value);
        Assert.Equal(0, surface.RemainingKeys);
    }

    [Fact]
    public async Task StandalonePickerEscapeCancels()
    {
        var editor = CreateEditor(new FakeSurface([Key(ConsoleKey.Escape)]));

        Assert.Empty(await editor.PickAsync("Choose", [new("one", "First")], cancellationToken: Token));
    }

    [Theory]
    [InlineData("Human Approval")]
    [InlineData("docs\\release notes.md")]
    [InlineData("say \"hello\"")]
    [InlineData("")]
    [InlineData("don't")]
    public void InsertedValuesRoundTripThroughCommandParser(string value)
    {
        Assert.Equal(value, Assert.Single(CommandLineArguments.Parse(CommandLineArguments.QuoteIfNeeded(value))));
    }

    [Fact]
    public async Task TerminalResolverChainsCommandsAndIncludesWorkflowAgents()
    {
        var session = new TerminalSelectionSession();
        var provider = new InMemoryTerminalCatalogProvider(new(
            [new("writer", "Writer", TerminalDocumentKind.Agent, "body")],
            [new("demo", "Demo", "bogus workflow", "Start --> End")]));
        var registry = DemoTestCommands.Create(provider, session);
        var resolver = new TerminalCompletionResolver(registry);
        var surface = new FakeSurface(
        [
            .. Text("/use"), Key(ConsoleKey.Enter), Key(ConsoleKey.Enter),
            Key(ConsoleKey.Enter), Key(ConsoleKey.Enter)
        ]);
        var editor = CreateEditor(surface, resolver);
        var line = await editor.ReadLineAsync("You> ", Token);

        Assert.Equal("/use agent demo", line);
        var handler = new TerminalCommandHandler(registry);
        await handler.HandleAsync(line, Token);
        Assert.Equal("demo", session.Current.WorkflowId);
        Assert.Contains("[w] Demo", surface.Output.ToString());
    }

    [Fact]
    public async Task TerminalHostActuallyUsesInjectedKeyEditor()
    {
        var console = new FakeConsole();
        var surface = new FakeSurface([.. Text("/exit"), Key(ConsoleKey.Enter)]);
        var session = new TerminalSelectionSession();
        var provider = new InMemoryTerminalCatalogProvider(TerminalCatalog.Empty);
        var registry = DemoTestCommands.Create(provider, session);
        var editor = new InteractiveLineEditor(console, new TerminalCompletionResolver(registry), surface);
        var service = new TerminalClientService(console, new RecordingDispatcher(),
            lineEditor: editor);

        await service.RunAsync(Token);

        Assert.Contains("Goodbye.", console.Output.ToString());
        Assert.Equal(0, console.LineReads);
        Assert.Equal(0, surface.RemainingKeys);
    }

    private static InteractiveLineEditor CreateEditor(FakeSurface surface, IOptionPickerResolver? resolver = null) =>
        new(new FakeConsole(), resolver, surface);

    private static ConsoleKeyInfo Character(char character) => new(character, 0, false, false, false);
    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);
    private static IEnumerable<ConsoleKeyInfo> Text(string text) => text.Select(Character);

}
