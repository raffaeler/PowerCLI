using PowerCLI;
using Xunit;

namespace PowerCLI.Tests;

public sealed class CommandCompletionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RootsAliasesAndLeadingWhitespaceUseRegisteredCommandsOnly()
    {
        var resolver = new TerminalCompletionResolver(ConfigurableCommandTests.Create(ConfigurableCommandTests.Form("status")));
        var picker = await resolver.ResolvePickerAsync("  /", Token);
        Assert.Equal(2, picker!.ReplacementStart);
        Assert.Contains(picker.Options, option => option.Label == "/x");
        Assert.Contains(picker.Options, option => option.Label == "/alias");
        Assert.Contains(picker.Options, option => option.Label == "/help");
        Assert.DoesNotContain(picker.Options, option => option.Label == "/exit");
        Assert.Null(await resolver.ResolvePickerAsync("plain prompt", Token));
    }

    [Fact]
    public async Task StaticChoicesAndNamedValuesUseTheSameGrammar()
    {
        var resolver = new TerminalCompletionResolver(ConfigurableCommandTests.Create(ConfigurableCommandTests.ExportForm()));
        Assert.Contains((await resolver.ResolvePickerAsync("/x target --format ", Token))!.Options, option => option.Value == "json");
        var equals = await resolver.ResolvePickerAsync("/x target -f=js", Token);
        Assert.Equal(13, equals!.ReplacementStart);
        Assert.Equal(2, equals.ReplacementLength);
        Assert.Equal("js", equals.Filter);
        Assert.Contains(equals.Options, option => option.Value == "json");
        var incomplete = await resolver.ResolvePickerAsync("/x target --format \"j", Token);
        Assert.Equal("j", incomplete!.Filter);
        Assert.Contains(incomplete.Options, option => option.Value == "json");
    }

    [Fact]
    public async Task UsedOptionsAreSuppressedIncludingAliasesAndCursorSuffix()
    {
        var resolver = new TerminalCompletionResolver(ConfigurableCommandTests.Create(ConfigurableCommandTests.ExportForm()));
        var picker = await resolver.ResolvePickerAsync("/x target -f json --force ", Token);
        Assert.DoesNotContain(picker!.Options, option => option.Value.StartsWith("--format", StringComparison.Ordinal) || option.Value.StartsWith("-f", StringComparison.Ordinal));
        Assert.DoesNotContain(picker.Options, option => option.Value == "--force");
        Assert.Contains(picker.Options, option => option.Value.StartsWith("--tag", StringComparison.Ordinal));
        const string input = "/x target  --force";
        picker = await resolver.ResolvePickerAsync(new CompletionRequest(input, 10), Token);
        Assert.DoesNotContain(picker!.Options, option => option.Value == "--force");
        Assert.Null(await resolver.ResolvePickerAsync("/x target -- ", Token));
    }

    [Fact]
    public async Task CursorReplacementPreservesEntireQuotedTokenAndSuffix()
    {
        var provider = new TestChoiceProvider { Options = [new("Human Approval", "Human approval")] };
        var form = ConfigurableCommandTests.Form("<a> <b>", "a,b") with
        {
            Arguments = new Dictionary<string, CommandValueDefinition>
            { ["a"] = new() { Choices = new() { Provider = "p" } }, ["b"] = new() }
        };
        var resolver = new TerminalCompletionResolver(ConfigurableCommandTests.Create(new(), provider, null, form));
        const string input = "/x \"Hu old\" PreserveMe";
        var picker = await resolver.ResolvePickerAsync(new CompletionRequest(input, 6), Token);
        Assert.Equal(3, picker!.ReplacementStart);
        Assert.Equal(8, picker.ReplacementLength);
        Assert.Equal("Hu", picker.Filter);
        var replacement = input.Remove(picker.ReplacementStart!.Value, picker.ReplacementLength!.Value)
            .Insert(picker.ReplacementStart.Value, CommandLineArguments.QuoteIfNeeded(picker.Options[0].Value));
        Assert.Equal("/x \"Human Approval\" PreserveMe", replacement);
    }

    [Fact]
    public async Task EqualValueReplacementPreservesQuotesAndLaterOptionsAcrossAlternativeBranches()
    {
        var form = ConfigurableCommandTests.Form("(one | two) <target> [--format <format>]", "target") with
        { Options = new Dictionary<string, CommandOptionDefinition>
            { ["--format"] = new() { ValueName = "format", Choices = new() { Values = ["json", "text"] } } } };
        var resolver = new TerminalCompletionResolver(ConfigurableCommandTests.Create(form));
        const string input = "/x one target --format=\"js old\" suffix";
        var cursor = input.IndexOf("js", StringComparison.Ordinal) + 2;
        var picker = await resolver.ResolvePickerAsync(new CompletionRequest(input, cursor), Token);
        Assert.Equal("\"js old\"", input.Substring(picker!.ReplacementStart!.Value, picker.ReplacementLength!.Value));
        Assert.Equal("js", picker.Filter);
        Assert.Equal(" suffix", input[(picker.ReplacementStart.Value + picker.ReplacementLength.Value)..]);
        picker = await resolver.ResolvePickerAsync(new CompletionRequest(input, input.IndexOf("--format", StringComparison.Ordinal) + 4), Token);
        Assert.Equal("--format", input.Substring(picker!.ReplacementStart!.Value, picker.ReplacementLength!.Value));
    }

    [Fact]
    public async Task RepeatedMenuReplacesOnlyItsSegmentAndPreservesFollowingOptions()
    {
        var form = ConfigurableCommandTests.Form("skills <ids>* [--force]", "ids") with
        {
            Arguments = new Dictionary<string, CommandValueDefinition> { ["ids"] = new() { Choices = new() { Values = ["one", "two", "three"], Selection = "multiple" } } },
            Options = new Dictionary<string, CommandOptionDefinition> { ["--force"] = new() { Type = "boolean" } }
        };
        var resolver = new TerminalCompletionResolver(ConfigurableCommandTests.Create(form));
        const string input = "/x skills one two --force";
        var picker = await resolver.ResolvePickerAsync(new CompletionRequest(input, input.IndexOf("one", StringComparison.Ordinal) + 2), Token);
        Assert.Equal(OptionPickerMode.Multiple, picker!.Mode);
        Assert.Equal("one two", input.Substring(picker.ReplacementStart!.Value, picker.ReplacementLength!.Value));
        Assert.Contains(picker.Options, option => option.Value == "two" && option.IsSelected);
        Assert.Equal(" --force", input[(picker.ReplacementStart.Value + picker.ReplacementLength.Value)..]);
    }

    [Fact]
    public async Task RepeatedNamedOptionMenusReplaceContiguousOccurrencesWithoutDuplicatingThem()
    {
        var form = ConfigurableCommandTests.Form("<target> [--tag <tags>]*", "target") with
        {
            Options = new Dictionary<string, CommandOptionDefinition>
            { ["--tag"] = new() { ValueName = "tags", Choices = new() { Values = ["one", "two", "three"], Selection = "multiple" } } }
        };
        var resolver = new TerminalCompletionResolver(ConfigurableCommandTests.Create(form));
        const string input = "/x target --tag one --tag t";
        var picker = await resolver.ResolvePickerAsync(input, Token);
        Assert.Equal(OptionPickerMode.Multiple, picker!.Mode);
        Assert.Equal("--tag", picker.RepeatedOptionName);
        Assert.Equal("--tag one --tag t", input.Substring(picker.ReplacementStart!.Value, picker.ReplacementLength!.Value));
        Assert.Contains(picker.Options, option => option.Value == "one" && option.IsSelected);
    }

    [Fact]
    public async Task RegistryEditorChainsRequiredAndOptionalContexts()
    {
        var form = ConfigurableCommandTests.Form("load <first> [with <second>]", "first,second") with
        {
            Arguments = new Dictionary<string, CommandValueDefinition>
            {
                ["first"] = new() { Choices = new() { Values = ["Human Approval"] } },
                ["second"] = new() { Choices = new() { Values = ["say \"hello\""] } }
            }
        };
        var resolver = new TerminalCompletionResolver(ConfigurableCommandTests.Create(form));
        var surface = new FakeSurface([.. Text("/x "), Enter, Enter, Enter, Enter, Enter]);
        var editor = new InteractiveLineEditor(new FakeConsole(), resolver, surface);
        Assert.Equal("/x load \"Human Approval\" with \"say \\\"hello\\\"\"", await editor.ReadLineAsync("You> ", Token));
        Assert.Equal(0, surface.RemainingKeys);
    }

    [Fact]
    public async Task RegistryEditorCompletesInTheMiddleWithoutLosingSuffixOrSubmittingEarly()
    {
        var form = ConfigurableCommandTests.Form("<a> <b>", "a,b") with
        {
            Arguments = new Dictionary<string, CommandValueDefinition>
            { ["a"] = new() { Choices = new() { Values = ["Human Approval"] } }, ["b"] = new() }
        };
        var surface = new FakeSurface([.. Text("/x Hu PreserveMe"), Key(ConsoleKey.Home),
            .. Enumerable.Repeat(Key(ConsoleKey.RightArrow), 5), Enter, Enter]);
        var editor = new InteractiveLineEditor(new FakeConsole(), new TerminalCompletionResolver(ConfigurableCommandTests.Create(form)), surface);
        Assert.Equal("/x \"Human Approval\" PreserveMe", await editor.ReadLineAsync("You> ", Token));
        Assert.Equal(0, surface.RemainingKeys);
    }

    [Fact]
    public async Task RegistryEditorRepeatedSelectionsHonorProviderStateAndEmptyAcceptance()
    {
        var provider = new TestChoiceProvider { Mode = OptionPickerMode.Multiple, Options = [new("one", "one", true), new("two", "two")] };
        var form = ConfigurableCommandTests.Form("<ids>*", "ids") with
        { Arguments = new Dictionary<string, CommandValueDefinition> { ["ids"] = new() { Choices = new() { Provider = "p", Selection = "multiple" } } } };
        var surface = new FakeSurface([.. Text("/x "), Key(ConsoleKey.Spacebar), Enter, Enter]);
        var editor = new InteractiveLineEditor(new FakeConsole(), new TerminalCompletionResolver(ConfigurableCommandTests.Create(new(), provider, null, form)), surface);
        Assert.Equal("/x ", await editor.ReadLineAsync("You> ", Token));
    }

    [Fact]
    public async Task TypingAnExistingRepeatedValuePreservesItWhenAcceptingTheMenu()
    {
        var form = ConfigurableCommandTests.Form("<ids>*", "ids") with
        { Arguments = new Dictionary<string, CommandValueDefinition> { ["ids"] = new() { Choices = new() { Values = ["one", "two"], Selection = "multiple" } } } };
        var surface = new FakeSurface([.. Text("/x one"), Enter, Enter]);
        var editor = new InteractiveLineEditor(new FakeConsole(), new TerminalCompletionResolver(ConfigurableCommandTests.Create(form)), surface);
        Assert.Equal("/x one", await editor.ReadLineAsync("", Token));
    }

    [Fact]
    public async Task EndMarkerAllowsLeadingDashChoiceCompletion()
    {
        var form = ConfigurableCommandTests.Form("<a>", "a") with
        { Arguments = new Dictionary<string, CommandValueDefinition> { ["a"] = new() { Choices = new() { Values = ["-negative"] } } } };
        var resolver = new TerminalCompletionResolver(ConfigurableCommandTests.Create(form));
        Assert.Contains((await resolver.ResolvePickerAsync("/x -- -ne", Token))!.Options, option => option.Value == "-negative");
    }

    [Fact]
    public async Task RepeatedChoicesContainingEqualsAndTrailingSpacesAreNotTreatedAsOptions()
    {
        var form = ConfigurableCommandTests.Form("<ids>*", "ids") with
        { Arguments = new Dictionary<string, CommandValueDefinition> { ["ids"] = new() { Choices = new() { Values = ["one=two", "trailing "], Selection = "multiple" } } } };
        var resolver = new TerminalCompletionResolver(ConfigurableCommandTests.Create(form));
        var picker = await resolver.ResolvePickerAsync("/x one=t", Token);
        Assert.Equal(OptionPickerMode.Multiple, picker!.Mode);
        Assert.Null(picker.RepeatedOptionName);
        Assert.Contains((await resolver.ResolveAsync("/x ", Token)), option => option.Value == "trailing ");
    }

    [Fact]
    public async Task OptionalOnlyRootCanSubmitWithoutBeingForcedIntoItsOptionalMenu()
    {
        var form = ConfigurableCommandTests.Form("[status]");
        var surface = new FakeSurface([.. Text("/x"), Enter]);
        var editor = new InteractiveLineEditor(new FakeConsole(), new TerminalCompletionResolver(ConfigurableCommandTests.Create(form)), surface);
        Assert.Equal("/x", await editor.ReadLineAsync("", Token));
        Assert.Equal(0, surface.RemainingKeys);
    }

    [Fact]
    public async Task CompletingAnOptionValueBeforeThePositionalValueChainsToThatValue()
    {
        var form = ConfigurableCommandTests.Form("<target> [--format <format>]", "target") with
        {
            Arguments = new Dictionary<string, CommandValueDefinition> { ["target"] = new() { Choices = new() { Values = ["destination"] } } },
            Options = new Dictionary<string, CommandOptionDefinition>
            { ["--format"] = new() { ValueName = "format", Choices = new() { Values = ["json"] } } }
        };
        var surface = new FakeSurface([.. Text("/x --format "), Enter, Enter, Enter]);
        var editor = new InteractiveLineEditor(new FakeConsole(), new TerminalCompletionResolver(ConfigurableCommandTests.Create(form)), surface);
        Assert.Equal("/x --format json destination", await editor.ReadLineAsync("", Token));
        Assert.Equal(0, surface.RemainingKeys);
    }

    [Fact]
    public async Task RegistryEditorInsertsRepeatedOptionOccurrencesAndParsesTheResult()
    {
        var handler = new RecordingCommandHandler();
        var form = ConfigurableCommandTests.Form("<target> [--tag <tags>]*", "target") with
        {
            Options = new Dictionary<string, CommandOptionDefinition>
            { ["--tag"] = new() { ValueName = "tags", Choices = new() { Values = ["one", "two"], Selection = "multiple" } } }
        };
        var registry = ConfigurableCommandTests.Create(handler, form);
        var surface = new FakeSurface([.. Text("/x target --tag "), Key(ConsoleKey.Spacebar), Key(ConsoleKey.DownArrow),
            Key(ConsoleKey.Spacebar), Enter, Enter]);
        var editor = new InteractiveLineEditor(new FakeConsole(), new TerminalCompletionResolver(registry), surface);
        var line = await editor.ReadLineAsync("", Token);
        Assert.Equal("/x target --tag one --tag two", line);
        Assert.Null((await new TerminalCommandHandler(registry).HandleAsync(line, Token)).Diagnostics);
        Assert.Equal(["one", "two"], handler.Invocations[0]["--tag"].Strings);
    }

    [Fact]
    public async Task CompletionFailureIsRenderedWithoutTerminalControls()
    {
        var provider = new TestChoiceProvider { Failure = new InvalidOperationException("failed\u001b[31m") };
        var form = ConfigurableCommandTests.Form("<a>", "a") with
        { Arguments = new Dictionary<string, CommandValueDefinition> { ["a"] = new() { Choices = new() { Provider = "p" } } } };
        var surface = new FakeSurface([.. Text("/x "), Key(ConsoleKey.Escape), Enter]);
        var editor = new InteractiveLineEditor(new FakeConsole(), new TerminalCompletionResolver(ConfigurableCommandTests.Create(new(), provider, null, form)), surface);
        Assert.Equal("/x ", await editor.ReadLineAsync("", Token));
        Assert.Contains("Completion failed:", surface.Output.ToString());
        Assert.DoesNotContain("\u001b", surface.Output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderLabelsAreSafeForCompletionClientsAndUnsafeValuesFailVisibly()
    {
        var provider = new TestChoiceProvider { Options = [new("value", "Label\u001b[31m\nSecond")] };
        var form = ConfigurableCommandTests.Form("<a>", "a") with
        { Arguments = new Dictionary<string, CommandValueDefinition> { ["a"] = new() { Choices = new() { Provider = "p" } } } };
        var resolver = new TerminalCompletionResolver(ConfigurableCommandTests.Create(new(), provider, null, form));
        var completion = Assert.Single(await resolver.ResolveAsync("/x ", Token));
        Assert.DoesNotContain("\u001b", completion.Label, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", completion.Label, StringComparison.Ordinal);
        provider.Options = [new("value\u001b", "Label")];
        Assert.Contains("control-containing", (await resolver.ResolvePickerAsync("/x ", Token))!.Error);
        Assert.Throws<CommandConfigurationException>(() => ConfigurableCommandTests.Create(form with
        { Arguments = new Dictionary<string, CommandValueDefinition> { ["a"] = new() { Choices = new() { Values = ["value\u001b"] } } } }));
    }

    private static ConsoleKeyInfo Enter => Key(ConsoleKey.Enter);
    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);
    private static IEnumerable<ConsoleKeyInfo> Text(string text) => text.Select(character => new ConsoleKeyInfo(character, 0, false, false, false));
}
