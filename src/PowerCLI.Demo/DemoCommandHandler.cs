using PowerCLI;

namespace PowerCLI.Demo;

public sealed class DemoCommandHandler(ITerminalCatalogProvider catalog, TerminalSelectionSession selection,
    ITerminalToolCatalog tools, DemoInputHandler? inputHandler = null, ITerminalWorkflowRunner? workflowRunner = null) : ICommandHandler
{
    private readonly ITerminalCatalogProvider _catalog = catalog;
    private readonly TerminalSelectionSession _selection = selection;
    private readonly ITerminalToolCatalog _tools = tools;
    private readonly DemoInputHandler _inputHandler = inputHandler ?? new(catalog, selection, tools, workflowRunner: workflowRunner);

    public async ValueTask<TerminalCommandResult> ExecuteAsync(CommandInvocation invocation, CancellationToken cancellationToken = default)
    {
        switch (invocation.FormId)
        {
            case "clear": return new(true, [], ClearScreen: true);
            case "exit": return new(true, ["Goodbye."], ExitRequested: true);
            case "new": case "use-clear": return Result(_selection.Reset());
            case "session": case "use-status": return Status();
            case "use-target": return Result(_selection.SetTarget(invocation["path"].String));
            case "export": return TerminalCommandResult.Message(
                $"Bogus export to '{invocation["target"].String}', format={invocation["--format"].String}, force={invocation["--force"].Boolean}, tags={string.Join(", ", invocation["--tag"].Strings)}.");
            case "tools":
                var tools = await _tools.GetToolsAsync(cancellationToken);
                return TerminalCommandResult.Message(tools.Count == 0 ? "No tools are available." :
                    string.Join('\n', tools.Select(tool => $"- {Safe(tool.Id)}: {Safe(tool.Description)}{(tool.IsTrusted ? "" : " (untrusted)")}")));
        }
        // Re-fetch and revalidate at the operation boundary; completion and dispatcher snapshots are not authorization.
        var snapshot = await _catalog.GetCatalogAsync(cancellationToken);
        switch (invocation.FormId)
        {
            case "use-agent":
                var id = invocation["id"].String;
                return Result(invocation.Literals[0] == "agent" && snapshot.FindDocument(id) is { Kind: TerminalDocumentKind.Agent }
                    ? _selection.SelectAgent(snapshot, id) : _selection.SelectWorkflow(snapshot, id));
            case "use-skill": return Result(_selection.SetSkills(snapshot, invocation["ids"].Strings));
            case "use-instruction": return Result(_selection.SetInstructions(snapshot, invocation["ids"].Strings));
            case "workflow":
                var workflow = snapshot.FindWorkflow(invocation["id"].String);
                return workflow is { IsTrusted: true, IsSubworkflow: false, AcceptsPrompt: true }
                    ? new(true, [$"Running workflow '{Safe(workflow.DisplayName)}'."],
                        Output: _inputHandler.RunWorkflowAsync(workflow.Id, cancellationToken: cancellationToken))
                    : TerminalCommandResult.Message("Workflow is unavailable or untrusted.");
            case "print":
                var kind = invocation.Literals[0];
                if (kind == "workflow")
                {
                    var printable = snapshot.FindWorkflow(invocation["id"].String);
                    return printable is { IsTrusted: true }
                        ? TerminalCommandResult.Message($"{Safe(printable.DisplayName)}\n{Safe(printable.Description)}\n\n{Safe(printable.Diagram)}")
                        : TerminalCommandResult.Message("Workflow is unavailable or untrusted.");
                }
                var expected = Kind(kind);
                var document = snapshot.FindDocument(invocation["id"].String);
                return document is { IsTrusted: true } && document.Kind == expected &&
                    (expected != TerminalDocumentKind.Instruction || document.IsEnabled)
                    ? TerminalCommandResult.Message(Safe(document.Body)) : TerminalCommandResult.Message("Document is unavailable, disabled, or untrusted.");
            case "workflows":
                var workflows = snapshot.Workflows.Where(workflow => workflow.IsTrusted).ToArray();
                return TerminalCommandResult.Message(workflows.Length == 0 ? "No trusted workflows are available." :
                    string.Join('\n', workflows.Select(workflow =>
                        $"- {Safe(workflow.Id)} [{(workflow.IsSubworkflow ? "subworkflow" : "workflow")}]: {Safe(workflow.DisplayName)}")));
            case "agents": case "skills": case "instructions":
                var listKind = Kind(invocation.FormId[..^1]);
                var documents = snapshot.OfKind(listKind).Where(document => document.IsTrusted &&
                    (listKind != TerminalDocumentKind.Instruction || document.IsEnabled)).ToArray();
                return TerminalCommandResult.Message(documents.Length == 0 ? $"No trusted {invocation.FormId} are available." :
                    string.Join('\n', documents.Select(document => $"- {Safe(document.Id)}: {Safe(document.DisplayName)}" +
                        (string.IsNullOrWhiteSpace(document.Description) ? "" : ": " + Safe(document.Description)))));
            default: throw new InvalidOperationException($"Unsupported demo form '{invocation.FormId}'.");
        }
    }

    private TerminalCommandResult Status()
    {
        var state = _selection.Current;
        return TerminalCommandResult.Message($"Agent: {state.AgentId ?? "(default)"}", $"Workflow: {state.WorkflowId ?? "(none)"}",
            $"Skills: {(state.SkillIds.Count == 0 ? "(none)" : string.Join(", ", state.SkillIds))}",
            $"Instructions: {(state.InstructionIds.Count == 0 ? "(none)" : string.Join(", ", state.InstructionIds))}", $"Target: {state.TargetPath}");
    }

    private static TerminalDocumentKind Kind(string kind) => kind switch
    {
        "agent" => TerminalDocumentKind.Agent,
        "skill" => TerminalDocumentKind.Skill,
        "instruction" => TerminalDocumentKind.Instruction,
        _ => throw new InvalidOperationException($"Unsupported document kind '{kind}'.")
    };
    private static TerminalCommandResult Result(TerminalOperationResult result) => TerminalCommandResult.Message(result.Message);
    private static string Safe(string text) => new(text.Where(character => !char.IsControl(character) || character is '\r' or '\n' or '\t').ToArray());
}
