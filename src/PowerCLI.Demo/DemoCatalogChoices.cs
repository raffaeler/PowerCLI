using PowerCLI;

namespace PowerCLI.Demo;

public sealed class DemoCatalogChoices(ITerminalCatalogProvider catalog, TerminalSelectionSession selection) : ICommandChoiceProvider
{
    private readonly ITerminalCatalogProvider _catalog = catalog;
    private readonly TerminalSelectionSession _selection = selection;

    public async ValueTask<CommandChoiceSnapshot> GetChoicesAsync(CommandChoiceContext context, CancellationToken cancellationToken = default)
    {
        var snapshot = await _catalog.GetCatalogAsync(cancellationToken);
        var invocation = context.Invocation;
        var kind = invocation.Literals.FirstOrDefault() ?? "workflow";
        var printing = invocation.Command == "/print";
        var multiple = invocation.FormId is "use-skill" or "use-instruction";
        var selected = kind == "skill" ? _selection.Current.SkillIds : kind == "instruction" ? _selection.Current.InstructionIds : [];
        var options = new List<TerminalOption>();
        if (kind is "agent" or "workflow")
        {
            if (kind == "agent")
                options.AddRange(snapshot.OfKind(TerminalDocumentKind.Agent).Where(document => document.IsTrusted)
                    .Select(document => new TerminalOption(document.Id, "[a] " + document.DisplayName)));
            if (kind == "workflow" || !printing)
                options.AddRange(snapshot.Workflows.Where(workflow => workflow.IsTrusted &&
                    (printing || workflow is { IsSubworkflow: false, AcceptsPrompt: true }))
                    .Select(workflow => new TerminalOption(workflow.Id, "[w] " + workflow.DisplayName)));
        }
        else
        {
            var documentKind = kind == "skill" ? TerminalDocumentKind.Skill : TerminalDocumentKind.Instruction;
            options.AddRange(snapshot.OfKind(documentKind).Where(document => document.IsTrusted &&
                    (documentKind != TerminalDocumentKind.Instruction || document.IsEnabled))
                .Select(document => new TerminalOption(document.Id, document.DisplayName, selected.Contains(document.Id, StringComparer.OrdinalIgnoreCase))));
        }
        return new(options.OrderBy(option => option.Value, StringComparer.OrdinalIgnoreCase).ToArray(),
            Mode: multiple ? OptionPickerMode.Multiple : OptionPickerMode.Single);
    }
}
