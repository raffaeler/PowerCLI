namespace PowerCLI.Demo;

/// <summary>An immutable snapshot of documents and workflows available to a demo session.</summary>
public sealed class TerminalCatalog
{
    public static TerminalCatalog Empty { get; } = new([], []);

    public TerminalCatalog(IEnumerable<TerminalDocument> documents, IEnumerable<TerminalWorkflow> workflows)
    {
        Documents = documents.ToArray();
        Workflows = workflows.ToArray();
    }

    public IReadOnlyList<TerminalDocument> Documents { get; }

    public IReadOnlyList<TerminalWorkflow> Workflows { get; }

    public IEnumerable<TerminalDocument> OfKind(TerminalDocumentKind kind) =>
        Documents.Where(document => document.Kind == kind);

    public TerminalDocument? FindDocument(string id) =>
        Documents.FirstOrDefault(document => string.Equals(document.Id, id, StringComparison.OrdinalIgnoreCase));

    public TerminalWorkflow? FindWorkflow(string id) =>
        Workflows.FirstOrDefault(workflow => string.Equals(workflow.Id, id, StringComparison.OrdinalIgnoreCase));
}
