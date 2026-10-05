namespace PowerCLI.Demo;

/// <summary>Manages trusted declarative and workflow selections for one demo session.</summary>
public sealed class TerminalSelectionSession
{
    private TerminalSelection _selection = TerminalSelection.Empty;

    public TerminalSelection Current => _selection;

    public TerminalOperationResult SelectAgent(TerminalCatalog catalog, string agentId)
    {
        var agent = catalog.FindDocument(agentId);
        if (agent is not { IsTrusted: true, Kind: TerminalDocumentKind.Agent })
        {
            return TerminalOperationResult.Failure($"Agent '{agentId}' is unavailable or untrusted.");
        }

        _selection = _selection with { AgentId = agent.Id, WorkflowId = null };
        return TerminalOperationResult.Success($"Using agent '{agent.DisplayName}'.");
    }

    public TerminalOperationResult SelectWorkflow(TerminalCatalog catalog, string workflowId)
    {
        var workflow = catalog.FindWorkflow(workflowId);
        if (workflow is not { IsTrusted: true, IsSubworkflow: false, AcceptsPrompt: true })
        {
            return TerminalOperationResult.Failure($"Workflow '{workflowId}' cannot be used as a terminal agent.");
        }

        _selection = _selection with { WorkflowId = workflow.Id, AgentId = null };
        return TerminalOperationResult.Success($"Using workflow '{workflow.DisplayName}'.");
    }

    public TerminalOperationResult SetSkills(TerminalCatalog catalog, IEnumerable<string> skillIds)
    {
        var requested = skillIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var invalid = requested.FirstOrDefault(id => catalog.FindDocument(id) is not { IsTrusted: true, Kind: TerminalDocumentKind.Skill });
        if (invalid is not null)
        {
            return TerminalOperationResult.Failure($"Skill '{invalid}' is unavailable or untrusted.");
        }

        _selection = _selection with { SkillIds = requested };
        return TerminalOperationResult.Success(requested.Length == 0 ? "Cleared skills." : $"Selected {requested.Length} skill(s).");
    }

    public TerminalOperationResult SetInstructions(TerminalCatalog catalog, IEnumerable<string> instructionIds)
    {
        var requested = instructionIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var invalid = requested.FirstOrDefault(id => catalog.FindDocument(id) is not { IsTrusted: true, IsEnabled: true, Kind: TerminalDocumentKind.Instruction });
        if (invalid is not null)
        {
            return TerminalOperationResult.Failure($"Instruction '{invalid}' is unavailable, disabled, or untrusted.");
        }

        _selection = _selection with { InstructionIds = requested };
        return TerminalOperationResult.Success(requested.Length == 0 ? "Cleared instructions." : $"Selected {requested.Length} instruction(s).");
    }

    public TerminalOperationResult SetTarget(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath) || Path.IsPathRooted(targetPath) ||
            targetPath.Split(['/', '\\']).Any(segment => segment == ".."))
        {
            return TerminalOperationResult.Failure("Target path must be a non-empty relative path without '..' segments.");
        }

        _selection = _selection with { TargetPath = targetPath };
        return TerminalOperationResult.Success($"Using target '{targetPath}'.");
    }

    public TerminalOperationResult ClearSelections()
    {
        _selection = TerminalSelection.Empty;
        return TerminalOperationResult.Success("Cleared declarative and workflow selections.");
    }

    public TerminalOperationResult Reset() => ClearSelections();
}
