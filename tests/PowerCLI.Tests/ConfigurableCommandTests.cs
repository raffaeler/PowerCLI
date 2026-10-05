using System.Globalization;
using PowerCLI;
using Xunit;

namespace PowerCLI.Tests;

public sealed class ConfigurableCommandTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EmptyRegistryOnlyBuildsInHelpAndPreservesPromptRouting()
    {
        var registry = new CommandRegistry(new());
        var dispatcher = new TerminalCommandHandler(registry);
        Assert.Equal(["/help"], registry.CommandNames);
        Assert.False((await dispatcher.HandleAsync("plain prompt", Token)).IsHandled);
        Assert.False((await dispatcher.HandleAsync(null, Token)).IsHandled);
        Assert.Contains("Unknown command", Assert.Single((await dispatcher.HandleAsync("/exit", Token)).Messages));
        Assert.Contains("/help", Assert.Single((await dispatcher.HandleAsync("  /HELP", Token)).Messages));
    }

    [Theory]
    [InlineData("""{"schemaVersion":2,"commands":[]}""")]
    [InlineData("""{"commands":[]}""")]
    [InlineData("""{"schemaVersion":1}""")]
    [InlineData("""{"schemaVersion":1,"commands":[],"extra":true}""")]
    [InlineData("""{"schemaVersion":1,"schemaVersion":1,"commands":[]}""")]
    [InlineData("""{"schemaVersion":1,"commands":[{"name":"/x","forms":[],"extra":1}]}""")]
    [InlineData("""{"schemaVersion":1,"commands":[{"name":"/x","forms":[{"id":"x","syntax":"","handler":"h","extra":1}]}]}""")]
    [InlineData("""{"schemaVersion":1,"commands":[{"name":"/x","forms":[{"id":"x","syntax":"<a>","handler":"h","arguments":{"a":{"extra":1}}}]}]}""")]
    [InlineData("""{"schemaVersion":1,"commands":[{"name":"/x","forms":[{"id":"x","syntax":"<a>","handler":"h","arguments":{"a":{"choices":{"extra":1}}}}]}]}""")]
    [InlineData("""{"schemaVersion":1,"commands":null}""")]
    [InlineData("""{"schemaVersion":1,"commands":[{"name":"/x","forms":[{"id":"x","syntax":"<a>","handler":"h","arguments":{"a":{"default":null}}}]}]}""")]
    public void JsonRejectsVersionsUnknownAndDuplicateProperties(string json) =>
        Assert.Throws<CommandConfigurationException>(() => CommandConfiguration.FromJson(json));

    [Fact]
    public async Task JsonStreamAndCodeContractsExecuteWithoutCatalogDependencies()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            """{"schemaVersion":1,"commands":[{"name":"/say","aliases":["/s"],"forms":[{"id":"say","syntax":"<text>","handler":"h","arguments":{"text":{}}}]}]}"""));
        var configuration = await CommandConfiguration.FromJsonAsync(stream, Token);
        var handler = new RecordingCommandHandler();
        var registry = Registry(configuration, handler);
        var result = await new TerminalCommandHandler(registry).HandleAsync("  /S \"MiXeD text\"", Token);
        Assert.Null(result.Diagnostics);
        var invocation = Assert.Single(handler.Invocations);
        Assert.Equal("/say", invocation.Command);
        Assert.Equal("MiXeD text", invocation["text"].String);
        Assert.True(invocation["text"].IsSupplied);
        Assert.Equal(5, Assert.Single(invocation.Sources["text"]).Start);
    }

    [Theory]
    [InlineData("[<a>] <b>", "a,b")]
    [InlineData("<a>* <b>", "a,b")]
    [InlineData("(<a> | literal)", "a")]
    [InlineData("(a | a)", "")]
    [InlineData("[a]*", "")]
    [InlineData("(a <a>)*", "a")]
    [InlineData("<a> <a>", "a")]
    [InlineData("<a>", "")]
    [InlineData("", "a")]
    [InlineData("[with <a>", "a")]
    [InlineData("(a | )", "")]
    [InlineData("()", "")]
    [InlineData("[--flag]+", "")]
    [InlineData("[--flag]*", "")]
    [InlineData("(a [--flag] | b)", "")]
    public void InvalidOrAmbiguousGrammarRejectedAtStartup(string syntax, string arguments)
    {
        var form = Form(syntax, arguments) with
        {
            Options = syntax.Contains("--flag", StringComparison.Ordinal)
                ? new Dictionary<string, CommandOptionDefinition> { ["--flag"] = new() { Type = "boolean" } } : new Dictionary<string, CommandOptionDefinition>()
        };
        Assert.Throws<CommandConfigurationException>(() => Create(form));
    }

    [Fact]
    public void FormsCannotBeDistinguishedByTypesChoicesOrNamedOptions()
    {
        var first = Form("<a>", "a");
        var second = Form("<b>", "b") with { Id = "second" };
        Assert.Throws<CommandConfigurationException>(() => Create(first, second));
    }

    [Theory]
    [InlineData("/help", "/alias")]
    [InlineData("/HELP", "/alias")]
    [InlineData("/x", "/help")]
    [InlineData("/x", "/X")]
    [InlineData("x", "/alias")]
    public void RootNamesAndAliasesAreValidated(string name, string alias)
    {
        var configuration = new CommandConfiguration { Commands = [new() { Name = name, Aliases = [alias], Forms = [Form("")] }] };
        Assert.Throws<CommandConfigurationException>(() => Registry(configuration, new()));
    }

    [Theory]
    [InlineData("integer", "not-an-int", null, null)]
    [InlineData("number", "NaN", null, null)]
    [InlineData("string", "abc", 0d, 2d)]
    [InlineData("integer", "2", 3d, 1d)]
    [InlineData("unknown", null, null, null)]
    public void InvalidTypesBoundsAndDefaultsAreRejected(string type, string? defaultValue, double? min, double? max)
    {
        var form = Form("[<a>]", "a") with { Arguments = new Dictionary<string, CommandValueDefinition>
            { ["a"] = new() { Type = type, Default = defaultValue, Min = min, Max = max } } };
        Assert.Throws<CommandConfigurationException>(() => Create(form));
    }

    [Fact]
    public void MissingBindingsUnusedMetadataAndOptionAliasCollisionsAreRejected()
    {
        Assert.Throws<CommandConfigurationException>(() => new CommandRegistry(new() { Commands = [new() { Name = "/x", Forms = [Form("")] }] }));
        Assert.Throws<CommandConfigurationException>(() => Create(Form("<a>", "a") with { Arguments = new Dictionary<string, CommandValueDefinition>
            { ["a"] = new() { Choices = new() { Provider = "missing" } } } }));
        Assert.Throws<CommandConfigurationException>(() => Create(Form("<a>", "a") with { Arguments = new Dictionary<string, CommandValueDefinition>
            { ["a"] = new() { Validator = "missing" } } }));
        var form = Form("[--first <one>] [--second <two>]") with { Options = new Dictionary<string, CommandOptionDefinition>
        {
            ["--first"] = new() { ValueName = "one", Aliases = ["-f"] },
            ["--second"] = new() { ValueName = "two", Aliases = ["-F"] }
        } };
        Assert.Throws<CommandConfigurationException>(() => Create(form));
        Assert.Throws<CommandConfigurationException>(() => Create(Form("") with { Options = new Dictionary<string, CommandOptionDefinition>
            { ["--unused"] = new() { Type = "boolean" } } }));
    }

    [Fact]
    public async Task LiteralsAlternativesOptionalSequencesAndTrailingRepetitionBind()
    {
        var handler = new RecordingCommandHandler();
        var form = Form("(agent | workflow) <id> [with <context>]", "id,context");
        var dispatcher = new TerminalCommandHandler(Create(handler, form));
        Assert.Null((await dispatcher.HandleAsync("/x AGENT 'Writer'", Token)).Diagnostics);
        Assert.Equal(["agent"], handler.Invocations[0].Literals);
        Assert.Empty(handler.Invocations[0]["context"].Items);
        Assert.Null((await dispatcher.HandleAsync("/x workflow demo WITH \"Some Context\"", Token)).Diagnostics);
        Assert.Equal("Some Context", handler.Invocations[1]["context"].String);
        var repeated = new TerminalCommandHandler(Create(handler, Form("compare <files>+", "files")));
        Assert.NotNull((await repeated.HandleAsync("/x compare", Token)).Diagnostics);
        Assert.Null((await repeated.HandleAsync("/x compare a b c", Token)).Diagnostics);
        Assert.Equal(["a", "b", "c"], handler.Invocations[^1]["files"].Strings);
        var zero = new TerminalCommandHandler(Create(handler, Form("skill <ids>*", "ids")));
        Assert.Null((await zero.HandleAsync("/x skill", Token)).Diagnostics);
        Assert.Empty(handler.Invocations[^1]["ids"].Items);
    }

    [Fact]
    public async Task NamedOptionsSupportAliasesEqualsAnywhereDefaultsFlagsAndEndMarker()
    {
        var handler = new RecordingCommandHandler();
        var dispatcher = new TerminalCommandHandler(Create(handler, ExportForm()));
        var result = await dispatcher.HandleAsync("/x --tag one -F=JSON target --force --tag 'two words'", Token);
        Assert.Null(result.Diagnostics);
        var invocation = Assert.Single(handler.Invocations);
        Assert.Equal("json", invocation["--format"].String);
        Assert.Equal(["one", "two words"], invocation["--tag"].Strings);
        Assert.True(invocation["--force"].Boolean);
        Assert.Equal("target", invocation["target"].String);
        await dispatcher.HandleAsync("/x -- -destination", Token);
        invocation = handler.Invocations[^1];
        Assert.Equal("-destination", invocation["target"].String);
        Assert.Equal("text", invocation["--format"].String);
        Assert.True(invocation["--format"].IsDefault);
        Assert.False(invocation["--force"].Boolean);
        Assert.False(invocation["--force"].IsSupplied);
    }

    [Theory]
    [InlineData("/x target --unknown")]
    [InlineData("/x target --format")]
    [InlineData("/x target --format=text -f json")]
    [InlineData("/x target --force --force")]
    [InlineData("/x target --force=true")]
    [InlineData("/x target --format xml")]
    [InlineData("/x target extra")]
    [InlineData("/x -negative")]
    [InlineData("/x \"unclosed")]
    [InlineData("/x target -ff json")]
    public async Task InvalidInputReturnsDiagnosticsUsageAndNeverExecutes(string input)
    {
        var handler = new RecordingCommandHandler();
        var result = await new TerminalCommandHandler(Create(handler, ExportForm())).HandleAsync(input, Token);
        Assert.NotEmpty(result.Diagnostics!);
        Assert.Empty(handler.Invocations);
        Assert.Contains(result.Messages, message => message.Contains("Usage:", StringComparison.Ordinal) || input.Contains("unclosed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RequiredRepeatableAndBooleanOptionsAreEnforced()
    {
        var handler = new RecordingCommandHandler();
        var form = Form("(--tag <tags>)+ --ready") with { Options = new Dictionary<string, CommandOptionDefinition>
        {
            ["--tag"] = new() { ValueName = "tags" },
            ["--ready"] = new() { Type = "boolean" }
        } };
        var dispatcher = new TerminalCommandHandler(Create(handler, form));
        Assert.NotNull((await dispatcher.HandleAsync("/x --ready", Token)).Diagnostics);
        Assert.NotNull((await dispatcher.HandleAsync("/x --tag one", Token)).Diagnostics);
        Assert.Null((await dispatcher.HandleAsync("/x --tag=one --ready --tag=two", Token)).Diagnostics);
        Assert.Equal(["one", "two"], Assert.Single(handler.Invocations)["--tag"].Strings);
    }

    [Theory]
    [InlineData("integer", "42", true)]
    [InlineData("integer", "4.2", false)]
    [InlineData("integer", "9223372036854775808", false)]
    [InlineData("number", "1.25e2", true)]
    [InlineData("number", "1,25", false)]
    [InlineData("number", "Infinity", false)]
    [InlineData("number", "NaN", false)]
    [InlineData("boolean", "TRUE", true)]
    [InlineData("boolean", "yes", false)]
    [InlineData("relativePath", "docs\\notes.md", true)]
    [InlineData("relativePath", "../secret", false)]
    [InlineData("relativePath", "C:\\secret", false)]
    [InlineData("relativePath", "/rooted", false)]
    [InlineData("relativePath", "", false)]
    public async Task ScalarTypesParseInvariantly(string type, string text, bool valid)
    {
        var handler = new RecordingCommandHandler();
        var form = Form("<a>", "a") with { Arguments = new Dictionary<string, CommandValueDefinition> { ["a"] = new() { Type = type } } };
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("it-IT");
            var result = await new TerminalCommandHandler(Create(handler, form)).HandleAsync("/x " + CommandLineArguments.QuoteIfNeeded(text), Token);
            Assert.Equal(valid, result.Diagnostics is null);
            Assert.Equal(valid ? 1 : 0, handler.Invocations.Count);
            if (valid)
            {
                var value = handler.Invocations[0]["a"];
                Assert.Equal(text, value.String);
                if (type == "integer") Assert.Equal(42, value.Integer);
                if (type == "number") Assert.Equal(125, value.Number);
                if (type == "boolean") Assert.True(value.Boolean);
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public async Task NumericAndStringBoundsAreInclusive()
    {
        var handler = new RecordingCommandHandler();
        var form = Form("<a> <b>", "a,b") with { Arguments = new Dictionary<string, CommandValueDefinition>
        {
            ["a"] = new() { Type = "integer", Min = 1, Max = 3 },
            ["b"] = new() { MinLength = 1, MaxLength = 3 }
        } };
        var dispatcher = new TerminalCommandHandler(Create(handler, form));
        Assert.Null((await dispatcher.HandleAsync("/x 1 abc", Token)).Diagnostics);
        Assert.Null((await dispatcher.HandleAsync("/x 3 a", Token)).Diagnostics);
        Assert.NotNull((await dispatcher.HandleAsync("/x 0 abc", Token)).Diagnostics);
        Assert.NotNull((await dispatcher.HandleAsync("/x 1 ''", Token)).Diagnostics);
        Assert.NotNull((await dispatcher.HandleAsync("/x 4 abcd", Token)).Diagnostics);
        Assert.Equal(2, handler.Invocations.Count);
    }

    [Fact]
    public async Task IntegerBoundsDoNotLosePrecisionAtDoubleThresholds()
    {
        var handler = new RecordingCommandHandler();
        var form = Form("<a>", "a") with { Arguments = new Dictionary<string, CommandValueDefinition>
        { ["a"] = new() { Type = "integer", Max = 9007199254740992d } } };
        var dispatcher = new TerminalCommandHandler(Create(handler, form));
        Assert.Null((await dispatcher.HandleAsync("/x 9007199254740992", Token)).Diagnostics);
        Assert.NotNull((await dispatcher.HandleAsync("/x 9007199254740993", Token)).Diagnostics);
        form = form with { Arguments = new Dictionary<string, CommandValueDefinition>
        { ["a"] = new() { Type = "integer", Min = -9223372036854775808d, Max = 9223372036854775808d } } };
        dispatcher = new TerminalCommandHandler(Create(handler, form));
        Assert.Null((await dispatcher.HandleAsync("/x 9223372036854775807", Token)).Diagnostics);
        Assert.Null((await dispatcher.HandleAsync("/x -- -9223372036854775808", Token)).Diagnostics);
    }

    [Fact]
    public async Task InputDiagnosticsIdentifyInvalidTokenAndQuoteSpans()
    {
        var dispatcher = new TerminalCommandHandler(Create(ExportForm()));
        var result = await dispatcher.HandleAsync("/x target --unknown", Token);
        var diagnostic = Assert.Single(result.Diagnostics!);
        Assert.Equal(10, diagnostic.Start);
        Assert.Equal(9, diagnostic.Length);
        result = await dispatcher.HandleAsync("/x \"unfinished", Token);
        diagnostic = Assert.Single(result.Diagnostics!);
        Assert.Equal(3, diagnostic.Start);
        Assert.Equal(11, diagnostic.Length);
        Assert.Contains(result.Messages, message => message.Contains("Usage:", StringComparison.Ordinal));
    }

    [Fact]
    public void NullCodeCollectionsAndRegistrationObjectsAreRejected()
    {
        Assert.Throws<CommandConfigurationException>(() => new CommandRegistry(new() { Commands = null! }));
        Assert.Throws<CommandConfigurationException>(() => Create(Form("") with { Options = null! }));
        Assert.Throws<CommandConfigurationException>(() => new CommandRegistry(new(),
            new Dictionary<string, ICommandHandler> { ["h"] = null! }));
    }

    [Fact]
    public void BoundsDefaultsAndChoiceModesMustAgreeWithTheirContracts()
    {
        var baseline = Form("[<a>]", "a");
        foreach (var definition in new CommandValueDefinition[]
        {
            new() { Type = "integer", Min = double.NaN },
            new() { MinLength = -1 },
            new() { MinLength = 4, MaxLength = 1 },
            new() { Default = "outside", Choices = new() { Values = ["inside"] } },
            new() { Choices = new() { Values = ["a", "A"] } },
            new() { Choices = new() { Values = ["a"], Provider = "p" } },
            new() { Choices = new() },
            new() { Choices = new() { Values = ["a"], Validation = "invalid" } },
            new() { Choices = new() { Values = ["a"], Selection = "invalid" } }
        })
            Assert.Throws<CommandConfigurationException>(() => Create(baseline with
            { Arguments = new Dictionary<string, CommandValueDefinition> { ["a"] = definition } }));
        Assert.Throws<CommandConfigurationException>(() => Create(Form("<a>", "a") with
        { Arguments = new Dictionary<string, CommandValueDefinition> { ["a"] = new() { Default = "value" } } }));
        Assert.Throws<CommandConfigurationException>(() => Create(Form("[--tag <tags>]+") with
        { Options = new Dictionary<string, CommandOptionDefinition> { ["--tag"] = new() { ValueName = "tags" } } }));
        var dynamicDefault = baseline with { Arguments = new Dictionary<string, CommandValueDefinition>
        { ["a"] = new() { Default = "Canonical", Choices = new() { Provider = "p" } } } };
        Assert.Throws<CommandConfigurationException>(() => Create(new(), new TestChoiceProvider(), null, dynamicDefault));
    }

    [Fact]
    public void ConfigurationDiagnosticsIncludeCommandFormAndSyntaxLocation()
    {
        var exception = Assert.Throws<CommandConfigurationException>(() => Create(Form("[<a>", "a")));
        Assert.Contains("$.commands[0].forms[0]", exception.Message);
        Assert.Contains("command '/x'", exception.Message);
        Assert.Contains("form 'form'", exception.Message);
        Assert.Contains("syntax offset", exception.Message);
    }

    [Fact]
    public void ShortOptionNamesMustBeAliasesNotCanonicalDeclarations()
    {
        Assert.Throws<CommandConfigurationException>(() => Create(Form("[-f <format>]") with
        { Options = new Dictionary<string, CommandOptionDefinition> { ["-f"] = new() { ValueName = "format" } } }));
    }

    [Fact]
    public async Task BooleanValuedOptionsAndEmptyQuotedStringsRemainDistinctFromAbsence()
    {
        var handler = new RecordingCommandHandler();
        var form = Form("<a> [--enabled <enabled>]", "a") with { Options = new Dictionary<string, CommandOptionDefinition>
        { ["--enabled"] = new() { Type = "boolean", ValueName = "enabled" } } };
        var dispatcher = new TerminalCommandHandler(Create(handler, form));
        Assert.Null((await dispatcher.HandleAsync("/x '' --enabled=false", Token)).Diagnostics);
        Assert.Equal("", handler.Invocations[0]["a"].String);
        Assert.True(handler.Invocations[0]["a"].IsSupplied);
        Assert.False(handler.Invocations[0]["--enabled"].Boolean);
        await dispatcher.HandleAsync("/x \"\"", Token);
        Assert.Empty(handler.Invocations[1]["--enabled"].Items);
        Assert.False(handler.Invocations[1]["--enabled"].IsSupplied);
    }

    [Fact]
    public async Task ClosedChoicesCanonicalizeButSuggestionsPreserveFreeTextAndDefaults()
    {
        var handler = new RecordingCommandHandler();
        var form = Form("[<a>] <b>*", "a,b") with { Arguments = new Dictionary<string, CommandValueDefinition>
        {
            ["a"] = new() { Default = "canonical", Choices = new() { Values = ["Canonical"] } },
            ["b"] = new() { Choices = new() { Values = ["known"], Validation = "suggestions" } }
        } };
        // Optional capture before a repeat is deliberately ambiguous and must be rejected.
        Assert.Throws<CommandConfigurationException>(() => Create(handler, form));
        form = Form("[<a>]", "a") with { Arguments = new Dictionary<string, CommandValueDefinition> { ["a"] = form.Arguments["a"] } };
        await new TerminalCommandHandler(Create(handler, form)).HandleAsync("/x", Token);
        Assert.Equal("Canonical", handler.Invocations[0]["a"].String);
        Assert.True(handler.Invocations[0]["a"].IsDefault);
        form = Form("<b>", "b") with { Arguments = new Dictionary<string, CommandValueDefinition> { ["b"] = new() { Choices = new() { Values = ["known"], Validation = "suggestions" } } } };
        await new TerminalCommandHandler(Create(handler, form)).HandleAsync("/x MiXeD", Token);
        Assert.Equal("MiXeD", handler.Invocations[^1]["b"].String);
    }

    [Fact]
    public async Task DynamicChoicesUseEarlierContextAndFreshSnapshots()
    {
        var handler = new RecordingCommandHandler();
        var provider = new TestChoiceProvider();
        var form = Form("agent <parent> <id>", "parent,id") with { Arguments = new Dictionary<string, CommandValueDefinition>
        { ["parent"] = new(), ["id"] = new() { Choices = new() { Provider = "p" } } } };
        var registry = Create(handler, provider, null, form);
        var resolver = new TerminalCompletionResolver(registry);
        Assert.Contains((await resolver.ResolveAsync("/x agent Earlier ", Token)), option => option.Value == "Canonical");
        Assert.Equal("Earlier", provider.Contexts[0].Invocation["parent"].String);
        Assert.Equal(["agent"], provider.Contexts[0].Invocation.Literals);
        provider.Options = [new("Changed", "Changed")];
        var dispatcher = new TerminalCommandHandler(registry);
        Assert.NotNull((await dispatcher.HandleAsync("/x agent Earlier Canonical", Token)).Diagnostics);
        Assert.Empty(handler.Invocations);
        Assert.Null((await dispatcher.HandleAsync("/x agent Earlier changed", Token)).Diagnostics);
        Assert.Equal("Changed", handler.Invocations[0]["id"].String);
    }

    [Fact]
    public async Task FailuresAreVisibleCancellationPropagatesAndValidatorsRunBeforeHandlers()
    {
        var handler = new RecordingCommandHandler();
        var provider = new TestChoiceProvider { Failure = new InvalidOperationException("provider unavailable") };
        var validator = new TestCommandValidator();
        var form = Form("<a>", "a") with { Arguments = new Dictionary<string, CommandValueDefinition>
            { ["a"] = new() { Choices = new() { Provider = "p" }, Validator = "v" } } };
        var registry = Create(handler, provider, validator, form);
        var dispatcher = new TerminalCommandHandler(registry);
        Assert.Contains("provider unavailable", string.Join('\n', (await dispatcher.HandleAsync("/x Canonical", Token)).Messages));
        var picker = await new TerminalCompletionResolver(registry).ResolvePickerAsync("/x ", Token);
        Assert.Contains("provider unavailable", picker!.Error);
        provider.Failure = new OperationCanceledException();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatcher.HandleAsync("/x Canonical", Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new TerminalCompletionResolver(registry).ResolveAsync("/x ", Token).AsTask());
        provider.Failure = null;
        validator.Error = "domain denied";
        Assert.Contains("domain denied", string.Join('\n', (await dispatcher.HandleAsync("/x canonical", Token)).Messages));
        Assert.Equal("Canonical", validator.Invocation!["a"].String);
        Assert.Empty(handler.Invocations);
        validator.Error = null;
        handler.Failure = new InvalidOperationException("handler failed");
        Assert.Contains("handler failed", string.Join('\n', (await dispatcher.HandleAsync("/x canonical", Token)).Messages));
    }

    [Fact]
    public async Task ChoiceCasePolicyAndCardinalityAreChecked()
    {
        var provider = new TestChoiceProvider { CaseSensitive = true };
        var handler = new RecordingCommandHandler();
        var form = Form("<a>", "a") with { Arguments = new Dictionary<string, CommandValueDefinition>
            { ["a"] = new() { Choices = new() { Provider = "p" } } } };
        var dispatcher = new TerminalCommandHandler(Create(handler, provider, null, form));
        Assert.NotNull((await dispatcher.HandleAsync("/x canonical", Token)).Diagnostics);
        Assert.Null((await dispatcher.HandleAsync("/x Canonical", Token)).Diagnostics);
        provider.Mode = OptionPickerMode.Multiple;
        Assert.Contains("cardinality", string.Join('\n', (await dispatcher.HandleAsync("/x Canonical", Token)).Messages));
        Assert.Throws<CommandConfigurationException>(() => Create(form with { Arguments = new Dictionary<string, CommandValueDefinition>
            { ["a"] = new() { Choices = new() { Values = ["a"], Selection = "multiple" } } } }));
    }

    [Fact]
    public async Task HandlerEffectsArePreservedExactly()
    {
        var output = new RecordingInputHandler().HandleAsync("sample", Token);
        var result = new TerminalCommandResult(true, ["effect"], ExitRequested: true, ClearScreen: true, Output: output);
        var handler = new RecordingCommandHandler { Result = result };
        Assert.Same(result, await new TerminalCommandHandler(Create(handler, Form(""))).HandleAsync("/x", Token));
        Assert.Single(handler.Invocations);
    }

    [Fact]
    public async Task ConfiguredHandlersCannotRerouteSlashInputToTheInputHandler()
    {
        var handler = new RecordingCommandHandler { Result = TerminalCommandResult.NotACommand };
        var result = await new TerminalCommandHandler(Create(handler, Form(""))).HandleAsync("/x", Token);
        Assert.True(result.IsHandled);
        Assert.Contains("must return a handled", string.Join('\n', result.Messages));
        Assert.NotNull(result.Diagnostics);
        Assert.Single(handler.Invocations);
    }

    [Fact]
    public async Task HelpIncludesFormsAliasesDefaultsDescriptionsExamplesAndSafeText()
    {
        var form = ExportForm() with { Examples = ["/x file --format json"] };
        var configuration = new CommandConfiguration { Commands = [new() { Name = "/x", Aliases = ["/alias"],
            Description = "Sample\u001b[31m", Forms = [form] }] };
        var registry = Registry(configuration, new());
        var dispatcher = new TerminalCommandHandler(registry);
        var detail = Assert.Single((await dispatcher.HandleAsync("/help x", Token)).Messages);
        Assert.Equal(detail, Assert.Single((await dispatcher.HandleAsync("/help /alias", Token)).Messages));
        Assert.Contains("default=text", detail);
        Assert.Contains("relativePath", detail);
        Assert.Contains("--tag <tags>: string, optional, repeated", detail);
        Assert.Contains("Example:", detail);
        Assert.Contains("/alias", detail);
        Assert.DoesNotContain("\u001b", detail, StringComparison.Ordinal);
        Assert.NotNull((await dispatcher.HandleAsync("/help missing", Token)).Diagnostics);
        Assert.NotNull((await dispatcher.HandleAsync("/help x extra", Token)).Diagnostics);
        Assert.Contains((await new TerminalCompletionResolver(registry).ResolveAsync("/help ", Token)), option => option.Value == "/x");
    }

    [Fact]
    public void CompiledRegistryIsASnapshotOfMutableConfiguration()
    {
        var forms = new List<CommandFormDefinition> { Form("") };
        var commands = new List<CommandDefinition> { new() { Name = "/x", Forms = forms } };
        var registry = Registry(new() { Commands = commands }, new());
        forms.Clear();
        commands.Clear();
        Assert.Contains("/x", registry.CommandNames);
        Assert.Contains("Usage: /x", registry.GetHelp("x"));
    }

    internal static CommandFormDefinition ExportForm() => Form("<target> [--format <format>] [--force] [--tag <tags>]*", "target") with
    {
        Arguments = new Dictionary<string, CommandValueDefinition> { ["target"] = new() { Type = "relativePath" } },
        Options = new Dictionary<string, CommandOptionDefinition>
        {
            ["--format"] = new() { ValueName = "format", Aliases = ["-f"], Default = "text", Choices = new() { Values = ["text", "json"] } },
            ["--force"] = new() { Type = "boolean" },
            ["--tag"] = new() { ValueName = "tags" }
        }
    };

    internal static CommandFormDefinition Form(string syntax, string arguments = "") => new()
    {
        Id = "form", Syntax = syntax, Handler = "h",
        Arguments = arguments.Split(',', StringSplitOptions.RemoveEmptyEntries).ToDictionary(name => name, _ => new CommandValueDefinition())
    };

    internal static CommandRegistry Create(params CommandFormDefinition[] forms) => Create(new RecordingCommandHandler(), forms);
    internal static CommandRegistry Create(RecordingCommandHandler handler, params CommandFormDefinition[] forms) => Create(handler, null, null, forms);
    internal static CommandRegistry Create(RecordingCommandHandler handler, TestChoiceProvider? provider, TestCommandValidator? validator,
        params CommandFormDefinition[] forms) =>
        Registry(new() { Commands = [new() { Name = "/x", Aliases = ["/alias"], Forms = forms }] }, handler, provider, validator);

    private static CommandRegistry Registry(CommandConfiguration configuration, RecordingCommandHandler handler,
        TestChoiceProvider? provider = null, TestCommandValidator? validator = null) =>
        new(configuration, new Dictionary<string, ICommandHandler> { ["h"] = handler },
            provider is null ? null : new Dictionary<string, ICommandChoiceProvider> { ["p"] = provider },
            validator is null ? null : new Dictionary<string, ICommandValidator> { ["v"] = validator });
}
