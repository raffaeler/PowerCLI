namespace PowerCLI.Demo;

/// <summary>Represents the catalog choices currently active for a demo conversation.</summary>
public sealed record TerminalSelection(
    string? AgentId,
    IReadOnlyList<string> SkillIds,
    IReadOnlyList<string> InstructionIds,
    string TargetPath,
    string? WorkflowId)
{
    public static TerminalSelection Empty { get; } = new(null, [], [], "README.md", null);
}
