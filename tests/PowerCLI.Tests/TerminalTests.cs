using PowerCLI;
using Xunit;

namespace PowerCLI.Tests;

public sealed class TerminalTests
{
    [Fact]
    public void Renderer_StripsFencesAndPreservesCodeStyle()
    {
        var renderer = new IncrementalMarkdownConsoleRenderer();
        var spans = renderer.Render("Before\n```csharp\nvar value = 1;\n```\nAfter");

        Assert.DoesNotContain(spans, span => span.Text.Contains("```", StringComparison.Ordinal));
        Assert.Contains(spans, span => span.Style == TerminalTextStyle.Code && span.Text.Contains("var value", StringComparison.Ordinal));
    }

    [Fact]
    public void Arguments_RespectQuotes()
    {
        var values = CommandLineArguments.Parse("/echo \"hello world\"");

        Assert.Equal(["/echo", "hello world"], values);
    }
}
