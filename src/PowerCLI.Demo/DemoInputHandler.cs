using System.Runtime.CompilerServices;
using PowerCLI;

namespace PowerCLI.Demo;

/// <summary>Adapts the sample agent, workflow, and tool domain to generic terminal output.</summary>
public sealed class DemoInputHandler : ITerminalInputHandler
{
    private readonly ITerminalCatalogProvider _catalog;
    private readonly TerminalSelectionSession _selection;
    private readonly ITerminalToolCatalog _tools;
    private readonly ITerminalConsole? _console;
    private readonly ITerminalAgent _agent;
    private readonly ITerminalWorkflowRunner _workflowRunner;
    private readonly IToolApprovalPolicy _approvalPolicy;

    /// <summary>Revalidates catalog trust before running sample agents and workflows.</summary>
    public DemoInputHandler(
        ITerminalCatalogProvider catalog,
        TerminalSelectionSession selection,
        ITerminalToolCatalog tools,
        ITerminalConsole? console = null,
        ITerminalAgent? agent = null,
        ITerminalWorkflowRunner? workflowRunner = null,
        IToolApprovalPolicy? approvalPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(tools);
        _catalog = catalog;
        _selection = selection;
        _tools = tools;
        _console = console;
        _agent = agent ?? new BogusAgent();
        _workflowRunner = workflowRunner ?? new BogusWorkflowRunner();
        _approvalPolicy = approvalPolicy ?? new BuiltInToolApprovalPolicy();
    }

    public async IAsyncEnumerable<TerminalOutput> HandleAsync(string input,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selection = _selection.Current;
        if (selection.WorkflowId is { } workflowId)
        {
            await foreach (var output in RunWorkflowAsync(workflowId, input, cancellationToken))
            {
                yield return output;
            }
            yield break;
        }

        var snapshot = await _catalog.GetCatalogAsync(cancellationToken);
        if (ValidateSelection(snapshot, selection) is { } error)
        {
            yield return Error(error);
            yield break;
        }

        await foreach (var activity in _agent.RespondAsync(input, selection, cancellationToken).WithCancellation(cancellationToken))
        {
            await foreach (var output in TranslateAsync(activity, cancellationToken))
            {
                yield return output;
            }
        }
    }

    public async IAsyncEnumerable<TerminalOutput> RunWorkflowAsync(string workflowId, string input = "",
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selection = _selection.Current;
        var snapshot = await _catalog.GetCatalogAsync(cancellationToken);
        var workflow = snapshot.FindWorkflow(workflowId);
        if (workflow is not { IsTrusted: true, IsSubworkflow: false, AcceptsPrompt: true })
        {
            yield return Error("Workflow is unavailable or untrusted.");
            yield break;
        }

        if (ValidateSelection(snapshot, selection) is { } error)
        {
            yield return Error(error);
            yield break;
        }

        var activities = await _workflowRunner.RunAsync(workflow.Id, input, selection, cancellationToken);
        foreach (var activity in activities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await foreach (var output in TranslateAsync(activity, cancellationToken))
            {
                yield return output;
            }
        }
    }

    private async IAsyncEnumerable<TerminalOutput> TranslateAsync(TerminalActivity activity,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (activity.Kind == TerminalActivityKind.ToolRequested)
        {
            yield return new($"Tool '{activity.ToolId}' requested.");
            yield return await InvokeToolAsync(activity, cancellationToken);
            yield break;
        }

        yield return activity.Kind switch
        {
            TerminalActivityKind.Reasoning => new(activity.Content, TerminalOutputKind.Markdown, "Thinking> ", TerminalTextStyle.Muted),
            TerminalActivityKind.Answer => new(activity.Content, TerminalOutputKind.Markdown, "Assistant> ", TerminalTextStyle.Bold),
            TerminalActivityKind.Status => new(activity.Content),
            TerminalActivityKind.Error => Error(activity.Content),
            _ => throw new InvalidOperationException($"Unsupported demo activity kind '{activity.Kind}'.")
        };
    }

    private async ValueTask<TerminalOutput> InvokeToolAsync(TerminalActivity activity, CancellationToken cancellationToken)
    {
        var tools = await _tools.GetToolsAsync(cancellationToken);
        var tool = tools.FirstOrDefault(candidate => string.Equals(candidate.Id, activity.ToolId, StringComparison.OrdinalIgnoreCase));
        if (tool is null)
        {
            return Error($"Tool '{activity.ToolId}' is unavailable.");
        }

        var invocation = new TerminalToolInvocation(activity.ToolArguments ?? new Dictionary<string, string>());
        if (_console is null || _console.IsInputRedirected || _console.IsOutputRedirected ||
            !await _approvalPolicy.ApproveAsync(tool, invocation, cancellationToken))
        {
            return Error($"Tool '{tool.Id}' was denied.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new(await tool.InvokeAsync(invocation, cancellationToken), Prefix: $"Tool '{tool.Id}' completed: ");
    }

    private static string? ValidateSelection(TerminalCatalog catalog, TerminalSelection selection)
    {
        if (selection.AgentId is { } agentId &&
            catalog.FindDocument(agentId) is not { IsTrusted: true, Kind: TerminalDocumentKind.Agent })
        {
            return $"Agent '{agentId}' is unavailable or untrusted.";
        }

        if (selection.WorkflowId is { } workflowId &&
            catalog.FindWorkflow(workflowId) is not { IsTrusted: true, IsSubworkflow: false, AcceptsPrompt: true })
        {
            return $"Workflow '{workflowId}' is unavailable or untrusted.";
        }

        foreach (var skillId in selection.SkillIds)
        {
            if (catalog.FindDocument(skillId) is not { IsTrusted: true, Kind: TerminalDocumentKind.Skill })
            {
                return $"Skill '{skillId}' is unavailable or untrusted.";
            }
        }

        foreach (var instructionId in selection.InstructionIds)
        {
            if (catalog.FindDocument(instructionId) is not { IsTrusted: true, IsEnabled: true, Kind: TerminalDocumentKind.Instruction })
            {
                return $"Instruction '{instructionId}' is unavailable, disabled, or untrusted.";
            }
        }

        return null;
    }

    private static TerminalOutput Error(string message) => new(message, Prefix: "Error> ");
}
