using PowerCLI;
using PowerCLI.Demo;
using Xunit;

namespace PowerCLI.Tests;

public sealed class TerminalTests
{
    [Fact]
    public async Task CommandHandler_SelectsTrustedAgentAndReportsStatus()
    {
        var catalog = CreateCatalog();
        var selection = new TerminalSelectionSession();
        var handler = new TerminalCommandHandler(DemoTestCommands.Create(new InMemoryTerminalCatalogProvider(catalog), selection));

        var result = await handler.HandleAsync("/use agent writer", TestContext.Current.CancellationToken);
        var status = await handler.HandleAsync("/use status", TestContext.Current.CancellationToken);

        Assert.True(result.IsHandled);
        Assert.Equal("writer", selection.Current.AgentId);
        Assert.Contains(status.Messages, message => message == "Agent: writer");
    }

    [Fact]
    public async Task Completion_OnlyOffersTrustedEnabledDocuments()
    {
        var selection = new TerminalSelectionSession();
        var resolver = new TerminalCompletionResolver(DemoTestCommands.Create(new InMemoryTerminalCatalogProvider(CreateCatalog()), selection));

        var completions = await resolver.ResolveAsync("/use instruction ", TestContext.Current.CancellationToken);

        Assert.Single(completions);
        Assert.Equal("safe", completions[0].Value);
    }

    [Fact]
    public void Renderer_StripsFencesAndPreservesCodeStyle()
    {
        var renderer = new IncrementalMarkdownConsoleRenderer();

        var spans = renderer.Render("Before\n```csharp\nvar value = 1;\n```\nAfter");

        Assert.DoesNotContain(spans, span => span.Text.Contains("```", StringComparison.Ordinal));
        Assert.Contains(spans, span => span.Style == TerminalTextStyle.Code && span.Text.Contains("var value", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ToolCatalog_AlwaysProvidesUtcTime()
    {
        var catalog = new TerminalToolCatalog(new FakeTimeProvider());

        var tool = Assert.Single(await catalog.GetToolsAsync(TestContext.Current.CancellationToken));
        var value = await tool.InvokeAsync(new TerminalToolInvocation(new Dictionary<string, string>()), TestContext.Current.CancellationToken);

        Assert.Equal("get_current_utc_time", tool.Id);
        Assert.Equal("2026-10-05T07:25:37.0000000+00:00", value);
    }

    [Fact]
    public void Arguments_RespectQuotes()
    {
        var values = CommandLineArguments.Parse("/use target \"docs/release notes.md\"");

        Assert.Equal(["/use", "target", "docs/release notes.md"], values);
    }

    private static TerminalCatalog CreateCatalog() =>
        new(
        [
            new("writer", "Writer", TerminalDocumentKind.Agent, "body"),
            new("safe", "Safe", TerminalDocumentKind.Instruction, "body"),
            new("off", "Off", TerminalDocumentKind.Instruction, "body", IsEnabled: false),
            new("untrusted", "Untrusted", TerminalDocumentKind.Instruction, "body", IsTrusted: false)
        ],
        []);

}
