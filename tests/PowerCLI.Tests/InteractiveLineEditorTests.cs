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

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(6)]
    public async Task EscapeClearsEntireInputAtAnyCursorPosition(int cursor)
    {
        var surface = new FakeSurface(
        [
            .. Text("abcdef"), Key(ConsoleKey.Home),
            .. Enumerable.Repeat(Key(ConsoleKey.RightArrow), cursor),
            Key(ConsoleKey.Escape), Key(ConsoleKey.Enter)
        ]);
        var editor = CreateEditor(surface);

        Assert.Equal("", await editor.ReadLineAsync("You> ", Token));
        Assert.Equal(0, surface.RemainingKeys);
    }

    [Fact]
    public async Task EscapeResetsCursorAndAllowsTypingAfterRepeatedClears()
    {
        var surface = new FakeSurface(
        [
            .. Text("old"), Key(ConsoleKey.LeftArrow), Key(ConsoleKey.Escape),
            Key(ConsoleKey.Escape), .. Text("new"), Key(ConsoleKey.Enter)
        ]);
        var editor = CreateEditor(surface);

        Assert.Equal("new", await editor.ReadLineAsync("You> ", Token));
    }

    [Fact]
    public async Task SecondEscapeClearsInputAfterDismissingMenu()
    {
        var surface = new FakeSurface(
        [
            Character('x'), Key(ConsoleKey.Escape), Key(ConsoleKey.Escape), Key(ConsoleKey.Enter)
        ]);
        var editor = CreateEditor(surface, new FixedResolver(new("", [new("xyz", "xyz")])));

        Assert.Equal("", await editor.ReadLineAsync("You> ", Token));
        Assert.Equal(0, surface.RemainingKeys);
    }

    [Fact]
    public async Task TypingAfterEscapeClearReopensCompletionWithoutSelectionOverrides()
    {
        var surface = new FakeSurface(
        [
            .. Text("/use "), Key(ConsoleKey.Spacebar), Key(ConsoleKey.Escape),
            Key(ConsoleKey.Escape), .. Text("/use "), Key(ConsoleKey.Enter), Key(ConsoleKey.Enter)
        ]);
        var editor = CreateEditor(surface,
            new FixedResolver(new("/use ", [new("one", "One", true)], OptionPickerMode.Multiple)));

        Assert.Equal("/use one", await editor.ReadLineAsync("You> ", Token));
    }

    [Fact]
    public async Task EscapeClearsRecalledInputWithoutRemovingHistory()
    {
        var editor = await CreateHistoryEditorAsync(["old"],
        [
            Key(ConsoleKey.UpArrow), Key(ConsoleKey.Escape), Key(ConsoleKey.Enter),
            Key(ConsoleKey.UpArrow), Key(ConsoleKey.Enter)
        ]);

        Assert.Equal("", await editor.ReadLineAsync(Token));
        Assert.Equal("old", await editor.ReadLineAsync(Token));
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
    public async Task TerminalResolverChainsIntoDemoMultipleChoiceMenu()
    {
        var app = new DemoApplication();
        var registry = app.CreateCommands();
        var resolver = new TerminalCompletionResolver(registry);
        var surface = new FakeSurface(
        [
            .. Text("/cho"), Key(ConsoleKey.Enter), Character(' '), Key(ConsoleKey.Spacebar),
            Key(ConsoleKey.Enter), Key(ConsoleKey.Enter)
        ]);
        var editor = CreateEditor(surface, resolver);
        var line = await editor.ReadLineAsync("You> ", Token);

        Assert.Equal("/choose concise", line);
        var handler = new TerminalCommandHandler(registry);
        await handler.HandleAsync(line, Token);
        Assert.Equal(["concise"], app.SelectedItems);
        Assert.Contains("with examples", surface.Output.ToString());
    }

    [Fact]
    public async Task TerminalHostActuallyUsesInjectedKeyEditor()
    {
        var console = new FakeConsole();
        var surface = new FakeSurface([.. Text("/exit"), Key(ConsoleKey.Enter)]);
        var registry = new DemoApplication().CreateCommands();
        var editor = new InteractiveLineEditor(console, new TerminalCompletionResolver(registry), surface);
        var service = new TerminalClientService(console, new RecordingDispatcher(),
            lineEditor: editor);

        await service.RunAsync(Token);

        Assert.Contains("Goodbye.", console.Output.ToString());
        Assert.Equal(0, console.LineReads);
        Assert.Equal(0, surface.RemainingKeys);
    }

    [Theory]
    [InlineData(1, 0, "third")]
    [InlineData(2, 0, "/second \"quoted value\"")]
    [InlineData(3, 0, "first")]
    [InlineData(5, 0, "first")]
    [InlineData(3, 1, "/second \"quoted value\"")]
    [InlineData(3, 2, "third")]
    [InlineData(3, 4, "")]
    public async Task HistoryNavigatesWithoutWrapping(int up, int down, string expected)
    {
        var editor = await CreateHistoryEditorAsync(["first", "/second \"quoted value\"", "third"],
        [
            .. Enumerable.Repeat(Key(ConsoleKey.UpArrow), up),
            .. Enumerable.Repeat(Key(ConsoleKey.DownArrow), down), Key(ConsoleKey.Enter)
        ]);

        Assert.Equal(expected, await editor.ReadLineAsync(Token));
    }

    [Fact]
    public async Task EmptyHistoryAndInitialDownLeaveDraftUntouched()
    {
        var editor = await CreateHistoryEditorAsync([],
        [
            .. Text("draft"), Key(ConsoleKey.UpArrow), Key(ConsoleKey.DownArrow), ControlR,
            Key(ConsoleKey.Enter), .. Text("next"), Key(ConsoleKey.DownArrow), ControlR, Key(ConsoleKey.Enter)
        ]);

        Assert.Equal("draft", await editor.ReadLineAsync(Token));
        Assert.Equal("next", await editor.ReadLineAsync(Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoryRestoresDraftAndCursor(bool remove)
    {
        var editor = await CreateHistoryEditorAsync(["old"],
        [
            .. Text("ac"), Key(ConsoleKey.LeftArrow), Key(ConsoleKey.UpArrow),
            remove ? ControlR : Key(ConsoleKey.DownArrow), ControlR,
            Character('b'), Key(ConsoleKey.Enter)
        ]);

        Assert.Equal("abc", await editor.ReadLineAsync(Token));
    }

    [Theory]
    [InlineData(1, "alpha")]
    [InlineData(2, "beta")]
    [InlineData(3, " alpha ")]
    [InlineData(4, "Alpha")]
    [InlineData(5, "alpha")]
    [InlineData(6, "alpha")]
    public async Task HistorySkipsBlankAndExactConsecutiveDuplicatesButPreservesOtherInputs(int up, string expected)
    {
        var editor = await CreateHistoryEditorAsync(["alpha", "alpha", "", "  ", "Alpha", " alpha ", "beta", "alpha"],
        [.. Enumerable.Repeat(Key(ConsoleKey.UpArrow), up), Key(ConsoleKey.Enter)]);

        Assert.Equal(expected, await editor.ReadLineAsync(Token));
    }

    [Fact]
    public async Task RemovingConsecutiveDuplicateLeavesNoExtraHistoryEntries()
    {
        var editor = await CreateHistoryEditorAsync(["same", "same", "", "   ", "same"],
        [
            Key(ConsoleKey.UpArrow), ControlR, Key(ConsoleKey.Enter),
            Key(ConsoleKey.UpArrow), Key(ConsoleKey.Enter)
        ]);

        Assert.Equal("", await editor.ReadLineAsync(Token));
        Assert.Equal("", await editor.ReadLineAsync(Token));
    }

    [Fact]
    public async Task CompletionRecordsOnlySubmittedLineNotIntermediateAcceptance()
    {
        var editor = await CreateHistoryEditorAsync([],
        [
            Character('/'), Key(ConsoleKey.Enter), Key(ConsoleKey.Enter), Key(ConsoleKey.Enter),
            Key(ConsoleKey.UpArrow), Key(ConsoleKey.UpArrow), Key(ConsoleKey.Enter)
        ], new TransitionResolver());

        Assert.Equal("/load \"Human Approval\"", await editor.ReadLineAsync(Token));
        Assert.Equal("/load \"Human Approval\"", await editor.ReadLineAsync(Token));
    }

    [Fact]
    public async Task ExactChoiceSubmissionIsRecorded()
    {
        var editor = await CreateHistoryEditorAsync([],
        [
            .. Text("/help"), Key(ConsoleKey.Enter),
            Key(ConsoleKey.Escape), Key(ConsoleKey.UpArrow), ControlR, Key(ConsoleKey.Enter),
            Key(ConsoleKey.Escape), Key(ConsoleKey.UpArrow), Key(ConsoleKey.Enter)
        ], new FixedResolver(new("", [new("/help", "/help")])));

        Assert.Equal("/help", await editor.ReadLineAsync(Token));
        Assert.Equal("", await editor.ReadLineAsync(Token));
        Assert.Equal("", await editor.ReadLineAsync(Token));
    }

    [Fact]
    public async Task EditedRecallIsSubmittedWithoutChangingStoredEntry()
    {
        var editor = await CreateHistoryEditorAsync(["old"],
        [
            Key(ConsoleKey.UpArrow), Key(ConsoleKey.Backspace), Character('D'), Key(ConsoleKey.Enter),
            Key(ConsoleKey.UpArrow), Key(ConsoleKey.UpArrow), Key(ConsoleKey.Enter)
        ]);

        Assert.Equal("olD", await editor.ReadLineAsync(Token));
        Assert.Equal("old", await editor.ReadLineAsync(Token));
    }

    [Fact]
    public async Task EditingKeepsHistoryPositionAndNavigationReplacesEdits()
    {
        var editor = await CreateHistoryEditorAsync(["first", "second"],
        [
            .. Text("draft"), Key(ConsoleKey.UpArrow), Character('!'),
            Key(ConsoleKey.UpArrow), Character('?'), Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter),
            .. Text("original"), Key(ConsoleKey.UpArrow), Character('!'), Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter)
        ]);

        Assert.Equal("second", await editor.ReadLineAsync(Token));
        Assert.Equal("original", await editor.ReadLineAsync(Token));
    }

    [Fact]
    public async Task RecalledCommandsAndRestoredDraftKeepCompletionClosed()
    {
        var editor = await CreateHistoryEditorAsync(["/load first", "/load second"],
        [
            .. Text("/load draft"), Key(ConsoleKey.Escape),
            Key(ConsoleKey.UpArrow), Key(ConsoleKey.UpArrow), Key(ConsoleKey.DownArrow),
            Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter)
        ], new TransitionResolver());

        Assert.Equal("/load draft", await editor.ReadLineAsync(Token));
    }

    [Fact]
    public async Task EditingRecalledCommandReopensChainedCompletion()
    {
        var editor = await CreateHistoryEditorAsync(["/load"],
        [
            Key(ConsoleKey.UpArrow), Character(' '), Key(ConsoleKey.Enter), Key(ConsoleKey.Enter)
        ], new TransitionResolver());

        Assert.Equal("/load \"Human Approval\"", await editor.ReadLineAsync(Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VisibleMenusKeepArrowPriorityAndPreventHistoryRemoval(bool multiple)
    {
        var editor = await CreateHistoryEditorAsync(["/stored"],
        [
            Key(ConsoleKey.UpArrow), .. Text("/"),
            ControlR, Key(ConsoleKey.DownArrow),
            .. multiple ? new[] { Key(ConsoleKey.Spacebar) } : Array.Empty<ConsoleKeyInfo>(),
            Key(ConsoleKey.Enter), Key(ConsoleKey.Enter),
            Key(ConsoleKey.UpArrow), Key(ConsoleKey.UpArrow), Key(ConsoleKey.Enter)
        ], new FixedResolver(new("/", [new("one", "One"), new("two", "Two")],
            multiple ? OptionPickerMode.Multiple : OptionPickerMode.Single,
            ReplacementStart: 0, ReplacementLength: 8, Filter: "")));

        Assert.Equal("two", await editor.ReadLineAsync(Token));
        Assert.Equal("/stored", await editor.ReadLineAsync(Token));
    }

    [Fact]
    public async Task HistoryRecallClearsMultipleSelectionOverrides()
    {
        var editor = await CreateHistoryEditorAsync(["seed"],
        [
            .. Text("/use "), Key(ConsoleKey.Spacebar), Key(ConsoleKey.Escape), Key(ConsoleKey.UpArrow),
            .. Enumerable.Repeat(Key(ConsoleKey.Backspace), 4), .. Text("/use "),
            Key(ConsoleKey.Enter), Key(ConsoleKey.Enter)
        ], new FixedResolver(new("/use ", [new("one", "One", true)], OptionPickerMode.Multiple)));

        Assert.Equal("/use one", await editor.ReadLineAsync(Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoryWorksWithEmptyOrErrorOnlyPicker(bool error)
    {
        var editor = await CreateHistoryEditorAsync(["old"],
        [Key(ConsoleKey.UpArrow), ControlR, Key(ConsoleKey.Enter), Key(ConsoleKey.UpArrow), Key(ConsoleKey.Enter)],
        new FixedResolver(new("", [], Error: error ? "Completion failed" : null)));

        Assert.Equal("", await editor.ReadLineAsync(Token));
        Assert.Equal("", await editor.ReadLineAsync(Token));
    }

    [Fact]
    public async Task FilteredOutChoicesAllowHistoryRecallAndRemoval()
    {
        var editor = await CreateHistoryEditorAsync(["old"],
        [
            .. Text("z"), Key(ConsoleKey.UpArrow), Character('z'), ControlR, Key(ConsoleKey.Enter),
            Key(ConsoleKey.Escape), Key(ConsoleKey.UpArrow), Key(ConsoleKey.Enter)
        ], new FixedResolver(new("", [new("alpha", "Alpha")])));

        Assert.Equal("z", await editor.ReadLineAsync(Token));
        Assert.Equal("z", await editor.ReadLineAsync(Token));
    }

    [Fact]
    public async Task EscapeAllowsHistoryBrowsingAfterVisibleChoices()
    {
        var editor = await CreateHistoryEditorAsync(["old"],
        [Key(ConsoleKey.Escape), Key(ConsoleKey.UpArrow), Key(ConsoleKey.Enter)],
        new FixedResolver(new("", [new("choice", "Choice")])));

        Assert.Equal("old", await editor.ReadLineAsync(Token));
    }

    [Theory]
    [InlineData(1, "draft", "oldest")]
    [InlineData(2, "newest", "oldest")]
    [InlineData(3, "middle", "middle")]
    public async Task RemovalSelectsNextNewerOrDraftAndPersists(int up, string afterRemoval, string remainingOlder)
    {
        var editor = await CreateHistoryEditorAsync(["oldest", "middle", "newest"],
        [
            .. Text("draft"), .. Enumerable.Repeat(Key(ConsoleKey.UpArrow), up), ControlR, Key(ConsoleKey.Enter),
            Key(ConsoleKey.UpArrow), Key(ConsoleKey.UpArrow), Key(ConsoleKey.UpArrow), Key(ConsoleKey.Enter)
        ]);

        Assert.Equal(afterRemoval, await editor.ReadLineAsync(Token));
        Assert.Equal(remainingOlder, await editor.ReadLineAsync(Token));
    }

    [Fact]
    public async Task RepeatedRemovalKeepsIndexValidAndCanEmptyHistory()
    {
        var editor = await CreateHistoryEditorAsync(["one", "two", "three"],
        [
            Key(ConsoleKey.UpArrow), Key(ConsoleKey.UpArrow), Key(ConsoleKey.UpArrow),
            ControlR, ControlR, ControlR, ControlR, Key(ConsoleKey.UpArrow), Key(ConsoleKey.Enter),
            Key(ConsoleKey.UpArrow), Key(ConsoleKey.Enter)
        ]);

        Assert.Equal("", await editor.ReadLineAsync(Token));
        Assert.Equal("", await editor.ReadLineAsync(Token));
    }

    [Fact]
    public async Task RemovalDeletesOnlySelectedOccurrenceEvenAfterEditing()
    {
        var editor = await CreateHistoryEditorAsync(["same", "between", "same"],
        [
            Key(ConsoleKey.UpArrow), Character('!'), ControlR, Key(ConsoleKey.UpArrow), Key(ConsoleKey.Enter),
            Key(ConsoleKey.UpArrow), Key(ConsoleKey.UpArrow), Key(ConsoleKey.Enter)
        ]);

        Assert.Equal("between", await editor.ReadLineAsync(Token));
        Assert.Equal("same", await editor.ReadLineAsync(Token));
    }

    [Fact]
    public async Task NavigationAfterRemovalUsesAdjustedHistoryIndexes()
    {
        var editor = await CreateHistoryEditorAsync(["oldest", "middle", "newest"],
        [
            Key(ConsoleKey.UpArrow), Key(ConsoleKey.UpArrow), ControlR,
            Key(ConsoleKey.UpArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter)
        ]);

        Assert.Equal("newest", await editor.ReadLineAsync(Token));
    }

    [Fact]
    public async Task PlainRTypesNormally()
    {
        var editor = await CreateHistoryEditorAsync(["old"],
        [Key(ConsoleKey.UpArrow), new ConsoleKeyInfo('r', ConsoleKey.R, false, false, false), Key(ConsoleKey.Enter)]);

        Assert.Equal("oldr", await editor.ReadLineAsync(Token));
    }

    [Fact]
    public async Task StandalonePickersNeitherBrowseRemoveNorRecordHistory()
    {
        var editor = await CreateHistoryEditorAsync(["stored"],
        [
            ControlR, Key(ConsoleKey.UpArrow), Key(ConsoleKey.Enter),
            .. Text("zzz"), Key(ConsoleKey.UpArrow), ControlR, Key(ConsoleKey.Escape),
            Key(ConsoleKey.UpArrow), Key(ConsoleKey.Enter)
        ]);

        Assert.Equal("choice", Assert.Single(await editor.PickAsync("Choose", [new("choice", "Choice")], cancellationToken: Token)).Value);
        Assert.Empty(await editor.PickAsync("Choose", [new("choice", "Choice")], cancellationToken: Token));
        Assert.Equal("stored", await editor.ReadLineAsync(Token));
    }

    [Fact]
    public async Task HistoryIsNotSharedBetweenEditors()
    {
        var first = await CreateHistoryEditorAsync(["stored"], [Key(ConsoleKey.UpArrow), Key(ConsoleKey.Enter)]);
        var second = await CreateHistoryEditorAsync([], [Key(ConsoleKey.UpArrow), Key(ConsoleKey.Enter)]);

        Assert.Equal("stored", await first.ReadLineAsync(Token));
        Assert.Equal("", await second.ReadLineAsync(Token));
    }

    [Fact]
    public async Task CancelledReadDoesNotRecordPartialInputOrUndoRemoval()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var surface = new BlockingSurface(
        [
            .. Text("older"), Key(ConsoleKey.Enter), .. Text("newer"), Key(ConsoleKey.Enter),
            Key(ConsoleKey.UpArrow), ControlR, .. Text("unfinished")
        ]);
        var editor = new InteractiveLineEditor(new FakeConsole(), surface: surface);
        Assert.Equal("older", await editor.ReadLineAsync(Token));
        Assert.Equal("newer", await editor.ReadLineAsync(Token));
        var read = editor.ReadLineAsync(source.Token).AsTask();
        await surface.ReadStarted.Task.WaitAsync(Token);
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        surface.EnqueueKeys([Key(ConsoleKey.UpArrow), Key(ConsoleKey.Enter)]);

        Assert.Equal("older", await editor.ReadLineAsync(Token));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RedirectedReadsPreserveTextAndEndOfInputWithoutRawKeys(bool inputRedirected, bool outputRedirected)
    {
        var console = new FakeConsole(inputRedirected, outputRedirected);
        foreach (var input in new string?[] { " first ", " first ", "", "  ", "/help", null })
            console.Lines.Enqueue(input);
        var surface = new FakeSurface([]);
        var editor = new InteractiveLineEditor(console, surface: surface);

        foreach (var expected in new string?[] { " first ", " first ", "", "  ", "/help", null })
            Assert.Equal(expected, await editor.ReadLineAsync("You> ", Token));
        Assert.Empty(surface.Output.ToString());
        Assert.DoesNotContain('\u001b', console.Output.ToString());
    }

    [Fact]
    public async Task TerminalLoopDispatchesReusedAndEditedHistory()
    {
        var console = new FakeConsole();
        var surface = new FakeSurface(
        [
            .. Text("hello"), Key(ConsoleKey.Enter),
            Key(ConsoleKey.UpArrow), Character('!'), Key(ConsoleKey.Enter),
            .. Text("/clear"), Key(ConsoleKey.Enter),
            Key(ConsoleKey.UpArrow), Key(ConsoleKey.Enter),
            .. Text("/exit"), Key(ConsoleKey.Enter)
        ]);
        var dispatcher = new RecordingDispatcher();
        var handler = new RecordingInputHandler();
        var editor = new InteractiveLineEditor(console, surface: surface);

        await new TerminalClientService(console, dispatcher, handler, editor).RunAsync(Token);

        Assert.Equal(["hello", "hello!", "/clear", "/clear", "/exit"], dispatcher.Inputs);
        Assert.Equal(["hello", "hello!"], handler.Inputs);
        Assert.Equal(2, console.Clears);
        Assert.Equal(0, surface.RemainingKeys);
    }

    private static async Task<InteractiveLineEditor> CreateHistoryEditorAsync(
        IReadOnlyList<string> history, IEnumerable<ConsoleKeyInfo> keys, IOptionPickerResolver? resolver = null)
    {
        var surface = new FakeSurface([]);
        var editor = CreateEditor(surface, resolver);
        foreach (var input in history)
        {
            surface.EnqueueKeys(Text(input));
            if (resolver is not null &&
                await resolver.ResolvePickerAsync(new CompletionRequest(input, input.Length), Token) is not null)
            {
                surface.EnqueueKeys([Key(ConsoleKey.Escape)]);
            }

            surface.EnqueueKeys([Key(ConsoleKey.Enter)]);
            Assert.Equal(input, await editor.ReadLineAsync(Token));
        }

        surface.EnqueueKeys(keys);
        return editor;
    }

    private static InteractiveLineEditor CreateEditor(FakeSurface surface, IOptionPickerResolver? resolver = null) =>
        new(new FakeConsole(), resolver, surface);

    private static ConsoleKeyInfo Character(char character) => new(character, 0, false, false, false);
    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);
    private static ConsoleKeyInfo ControlR => new('\u0012', ConsoleKey.R, false, false, true);
    private static IEnumerable<ConsoleKeyInfo> Text(string text) => text.Select(Character);

}
