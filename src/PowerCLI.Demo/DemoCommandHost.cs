using PowerCLI;

namespace PowerCLI.Demo;

public static class DemoCommandHost
{
    public static CommandRegistry Create(CommandConfiguration configuration, ITerminalCatalogProvider catalog,
        TerminalSelectionSession selection, ITerminalToolCatalog tools, DemoInputHandler? inputHandler = null,
        ITerminalWorkflowRunner? workflowRunner = null) =>
        new(configuration, new Dictionary<string, ICommandHandler> { ["demo"] = new DemoCommandHandler(catalog, selection, tools, inputHandler, workflowRunner) },
            new Dictionary<string, ICommandChoiceProvider> { ["demo.catalog"] = new DemoCatalogChoices(catalog, selection) });
}
