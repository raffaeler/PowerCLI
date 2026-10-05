using PowerCLI;
using Xunit;

namespace PowerCLI.Tests;

public sealed class FluentCommandTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static TerminalCommandResult Echo(CommandInvocation input) => TerminalCommandResult.Message(input["text"].String);

    [Fact]
    public void EmptyBuilderOnlyRegistersHelp() =>
        Assert.Equal(["/help"], new CommandRegistryBuilder().Build().CommandNames);

    [Fact]
    public async Task FluentAndJsonConfigurationsHaveEquivalentHelpCompletionAndExecution()
    {
        var fluent = new CommandRegistryBuilder().Command("/echo", command => command
            .Description("Echo text.").Alias("/e")
            .Form("<text>", form => form.Argument("text", value => value.Description("Input."))
                .Option("--style", "style", option => option.Alias("-s").Choices("plain", "bold").Default("plain"))
                .Flag("--upper").Example("/echo hello").Handle(Echo))).Build();
        var json = CommandConfiguration.FromJson("""
            {"schemaVersion":1,"commands":[{"name":"/echo","description":"Echo text.","aliases":["/e"],"forms":[{
            "id":"echo","handler":"echo","syntax":"<text> [--style <style>] [--upper]",
            "arguments":{"text":{"description":"Input."}},
            "options":{"--style":{"valueName":"style","aliases":["-s"],"choices":{"values":["plain","bold"]},"default":"plain"},
            "--upper":{"type":"boolean"}},"examples":["/echo hello"]}]}]}
            """);
        var expected = new CommandRegistry(json,
            new Dictionary<string, ICommandHandler> { ["echo"] = new RecordingCommandHandler() });

        Assert.Equal(expected.GetHelp(), fluent.GetHelp());
        Assert.Equal(expected.GetHelp("echo"), fluent.GetHelp("echo"));
        var completion = await new TerminalCompletionResolver(fluent).ResolveAsync("/echo hello --style ", Token);
        Assert.Equal(await new TerminalCompletionResolver(expected).ResolveAsync("/echo hello --style ", Token), completion);
        Assert.Equal(["MiXeD text"], (await new TerminalCommandHandler(fluent).HandleAsync("/E \"MiXeD text\" -s BOLD", Token)).Messages);
        Assert.NotNull((await new TerminalCommandHandler(fluent).HandleAsync("/echo hi --style invalid", Token)).Diagnostics);
    }

    [Theory]
    [InlineData(false, false, false, "[--flag]", "/x", true)]
    [InlineData(false, true, false, "--flag", "/x", false)]
    [InlineData(false, true, false, "--flag", "/x --flag", true)]
    [InlineData(true, false, false, "[--value <value>]", "/x", true)]
    [InlineData(true, true, false, "--value <value>", "/x", false)]
    [InlineData(true, true, false, "--value <value>", "/x --value a", true)]
    [InlineData(true, false, true, "[--value <value>]*", "/x --value a --value b", true)]
    [InlineData(true, true, true, "(--value <value>)+", "/x", false)]
    [InlineData(true, true, true, "(--value <value>)+", "/x --value a --value b", true)]
    public async Task OptionDeclarationsGenerateTheirSyntaxAndCardinality(
        bool valued, bool required, bool repeated, string syntax, string input, bool valid)
    {
        var registry = new CommandRegistryBuilder().Command("/x", command => command.Form("", form =>
        {
            void Configure(CommandOptionBuilder option)
            {
                if (required) option.Required();
                if (repeated) option.Repeat();
            }
            if (valued) form.Option("--value", "value", Configure);
            else form.Flag("--flag", Configure);
            form.Handle(_ => TerminalCommandResult.Message("executed"));
        })).Build();

        Assert.Contains("Usage: /x " + syntax, registry.GetHelp("x"));
        var result = await new TerminalCommandHandler(registry).HandleAsync(input, Token);
        Assert.Equal(valid, result.Diagnostics is null);
        if (valid) Assert.Equal(["executed"], result.Messages);
    }

    [Fact]
    public async Task TypedValuesDefaultsBoundsAndRepeatedAliasesReachCallback()
    {
        CommandInvocation? captured = null;
        var registry = new CommandRegistryBuilder().Command("/x", command => command.Form("[<text>]", form => form
            .Argument("text", value => value.Length(1, 10).Default("sample"))
            .Option("--count", "count", option => option.Type("integer").Bounds(1, 3).Default("2"))
            .Option("--ratio", "ratio", option => option.Type("number").Bounds(0, 1).Default("0.5"))
            .Option("--tag", "tags", option => option.Alias("-t").Length(1, 5).Repeat())
            .Handle(input =>
            {
                captured = input;
                return TerminalCommandResult.Message("ok");
            }))).Build();
        var dispatcher = new TerminalCommandHandler(registry);

        Assert.Null((await dispatcher.HandleAsync("/x -t one --tag=two", Token)).Diagnostics);
        Assert.NotNull(captured);
        Assert.Equal("sample", captured["text"].String);
        Assert.True(captured["text"].IsDefault);
        Assert.Equal(2, captured["--count"].Integer);
        Assert.Equal(0.5, captured["--ratio"].Number);
        Assert.Equal(["one", "two"], captured["--tag"].Strings);
        Assert.NotNull((await dispatcher.HandleAsync("/x --count 4", Token)).Diagnostics);
        Assert.NotNull((await dispatcher.HandleAsync("/x --tag toolong", Token)).Diagnostics);
    }

    [Fact]
    public async Task LiteralDistinguishedFormsAndAlternativesUseSeparateCallbacks()
    {
        var registry = new CommandRegistryBuilder().Command("/x", command => command
            .Form("(add | append) <text>", form => form.Argument("text").Handle(Echo))
            .Form("clear", form => form.Handle(_ => TerminalCommandResult.Message("cleared")))).Build();
        var dispatcher = new TerminalCommandHandler(registry);

        Assert.Equal(["quoted value"], (await dispatcher.HandleAsync("/x APPEND \"quoted value\"", Token)).Messages);
        Assert.Equal(["cleared"], (await dispatcher.HandleAsync("/x clear", Token)).Messages);
    }

    [Fact]
    public async Task DynamicMultipleChoicesRefreshForCompletionAndExecution()
    {
        var available = "one";
        var calls = 0;
        var executed = 0;
        var registry = new CommandRegistryBuilder().Command("/x", command => command.Form("<items>*", form => form
            .Argument("items", value => value.Multiple().Choices((context, cancellationToken) =>
            {
                Assert.Equal("items", context.ValueName);
                Assert.Equal(Token, cancellationToken);
                calls++;
                return ValueTask.FromResult(new CommandChoiceSnapshot([new(available, "Label", true)], Mode: OptionPickerMode.Multiple));
            }))
            .Handle(_ =>
            {
                executed++;
                return TerminalCommandResult.Message("ok");
            }))).Build();
        var picker = await new TerminalCompletionResolver(registry).ResolvePickerAsync("/x ", Token);
        Assert.NotNull(picker);
        Assert.True(Assert.Single(picker.Options).IsSelected);
        Assert.Equal(OptionPickerMode.Multiple, picker.Mode);
        available = "two";
        var dispatcher = new TerminalCommandHandler(registry);

        Assert.NotNull((await dispatcher.HandleAsync("/x one", Token)).Diagnostics);
        Assert.Null((await dispatcher.HandleAsync("/x two", Token)).Diagnostics);
        Assert.Equal(1, executed);
        Assert.True(calls >= 3);
    }

    [Fact]
    public async Task StaticChoiceSettingsPreserveSuggestionsAndCaseSensitivity()
    {
        var registry = new CommandRegistryBuilder().Command("/x", command => command
            .Form("closed <text>", form => form.Argument("text", value => value.CaseSensitive().Choices("MiXeD")).Handle(Echo))
            .Form("open <text>", form => form.Argument("text", value => value.Suggestions().Choices("suggested")).Handle(Echo))).Build();
        var dispatcher = new TerminalCommandHandler(registry);

        Assert.NotNull((await dispatcher.HandleAsync("/x closed mixed", Token)).Diagnostics);
        Assert.Null((await dispatcher.HandleAsync("/x closed MiXeD", Token)).Diagnostics);
        Assert.Equal(["other"], (await dispatcher.HandleAsync("/x open other", Token)).Messages);
    }

    [Fact]
    public async Task CustomValidatorReportsErrorsAndKeepsTypedValueAndToken()
    {
        var registry = new CommandRegistryBuilder().Command("/x", command => command.Form("<text>", form => form
            .Argument("text", value => value.Validate((invocation, name, input, cancellationToken) =>
            {
                Assert.Equal("/x", invocation.Command);
                Assert.Equal("text", name);
                Assert.Equal(Token, cancellationToken);
                return ValueTask.FromResult<string?>(input.String == "ok" ? null : "Must be ok.");
            })).Handle(Echo))).Build();
        var dispatcher = new TerminalCommandHandler(registry);

        Assert.Equal(["ok"], (await dispatcher.HandleAsync("/x ok", Token)).Messages);
        Assert.Contains("Must be ok.", string.Join('\n', (await dispatcher.HandleAsync("/x bad", Token)).Messages));
    }

    [Theory]
    [InlineData("handler")]
    [InlineData("provider")]
    [InlineData("validator")]
    public async Task AsyncCallbacksPropagateCancellation(string stage)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        async ValueTask Wait(CancellationToken token)
        {
            Assert.Equal(cancellation.Token, token);
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
        }
        var registry = new CommandRegistryBuilder().Command("/x", command => command.Form("<text>", form =>
        {
            form.Argument("text", value =>
            {
                if (stage == "provider") value.Choices(async (_, token) =>
                {
                    await Wait(token);
                    return new CommandChoiceSnapshot([new("ok", "ok")]);
                });
                if (stage == "validator") value.Validate(async (_, _, _, token) =>
                {
                    await Wait(token);
                    return null;
                });
            });
            form.Handle(async (_, token) =>
            {
                if (stage == "handler") await Wait(token);
                return TerminalCommandResult.Message("ok");
            });
        })).Build();
        var read = new TerminalCommandHandler(registry).HandleAsync("/x ok", cancellation.Token).AsTask();
        await started.Task.WaitAsync(Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
    }

    [Fact]
    public async Task CallbackFailuresAreReportedWithoutSuccessFallbacks()
    {
        var registry = new CommandRegistryBuilder().Command("/x", command => command.Form("", form =>
            form.Handle((_, _) => throw new InvalidOperationException("callback failure")))).Build();

        Assert.Contains("callback failure", string.Join('\n',
            (await new TerminalCommandHandler(registry).HandleAsync("/x", Token)).Messages));
    }

    [Fact]
    public async Task BuildsSnapshotMetadataAndAllowLaterBuilderChangesWithoutAccumulatingOptions()
    {
        CommandBuilder? commandBuilder = null;
        CommandOptionBuilder? optionBuilder = null;
        var builder = new CommandRegistryBuilder().Command("/x", command =>
        {
            commandBuilder = command;
            command.Form("", form => form.Option("--text", "text", option =>
            {
                optionBuilder = option;
                option.Default("first");
            }).Handle(input => TerminalCommandResult.Message(input["--text"].String)));
        });
        var first = builder.Build();
        Assert.NotNull(commandBuilder);
        Assert.NotNull(optionBuilder);
        commandBuilder.Alias("/alias");
        optionBuilder.Default("second");
        var second = builder.Build();
        var third = builder.Build();

        Assert.Equal(["first"], (await new TerminalCommandHandler(first).HandleAsync("/x", Token)).Messages);
        Assert.Equal(["second"], (await new TerminalCommandHandler(second).HandleAsync("/alias", Token)).Messages);
        Assert.Contains("Unknown command", string.Join('\n', (await new TerminalCommandHandler(first).HandleAsync("/alias", Token)).Messages));
        Assert.Equal(second.GetHelp("x"), third.GetHelp("x"));
        Assert.Equal(1, third.GetHelp("x").Split("[--text <text>]").Length - 1);
    }

    [Theory]
    [InlineData("duplicate-argument")]
    [InlineData("duplicate-option")]
    [InlineData("duplicate-handler")]
    [InlineData("duplicate-choices")]
    [InlineData("duplicate-validator")]
    [InlineData("missing-handler")]
    [InlineData("unused-argument")]
    [InlineData("missing-argument")]
    [InlineData("flag-repeat")]
    [InlineData("invalid-type")]
    [InlineData("incompatible-bounds")]
    [InlineData("choice-settings-without-choices")]
    [InlineData("multiple-scalar")]
    [InlineData("default-repeated")]
    [InlineData("duplicate-alias")]
    [InlineData("duplicate-command")]
    [InlineData("duplicate-option-syntax")]
    public void InvalidDeclarationsFailBeforeReadingInput(string error)
    {
        Assert.Throws<CommandConfigurationException>(() =>
        {
            var builder = new CommandRegistryBuilder().Command("/x", command => command.Form(
                error is "missing-argument" or "multiple-scalar" ? "<text>" :
                error == "duplicate-option-syntax" ? "[--flag]" : "", form =>
            {
                if (error == "duplicate-argument") form.Argument("text").Argument("text");
                if (error == "duplicate-option") form.Flag("--flag").Flag("--flag");
                if (error == "duplicate-handler") form.Handle(Echo).Handle(Echo);
                if (error == "unused-argument") form.Argument("text");
                if (error == "flag-repeat") form.Flag("--flag", option => option.Repeat());
                if (error == "invalid-type") form.Option("--value", "value", value => value.Type("unknown"));
                if (error == "incompatible-bounds") form.Option("--value", "value", value => value.Bounds(1, 2));
                if (error == "choice-settings-without-choices") form.Option("--value", "value", value => value.Multiple());
                if (error == "multiple-scalar") form.Argument("text", value => value.Choices("one").Multiple());
                if (error == "default-repeated") form.Option("--value", "value", value => value.Repeat().Default("one"));
                if (error == "duplicate-alias") form.Flag("--one", option => option.Alias("-x"))
                    .Flag("--two", option => option.Alias("-X"));
                if (error == "duplicate-option-syntax") form.Flag("--flag");
                if (error == "duplicate-choices") form.Option("--value", "value", value => value.Choices("one").Choices("two"));
                if (error == "duplicate-validator") form.Option("--value", "value", value =>
                    value.Validate((_, _, _, _) => ValueTask.FromResult<string?>(null))
                        .Validate((_, _, _, _) => ValueTask.FromResult<string?>(null)));
                if (error != "missing-handler") form.Handle(_ => TerminalCommandResult.Message("ok"));
            }));
            if (error == "duplicate-command") builder.Command("/X", command => command.Form("", form => form.Handle(Echo)));
            builder.Build();
        });
    }
}
