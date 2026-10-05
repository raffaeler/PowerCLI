using PowerCLI;
using PowerCLI.Demo;

namespace PowerCLI.Tests;

internal static class DemoTestCommands
{
    internal static CommandRegistry Create(ITerminalCatalogProvider provider, TerminalSelectionSession selection) =>
        DemoCommandHost.Create(CommandConfiguration.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "commands.json"))),
            provider, selection, new TerminalToolCatalog());
}
