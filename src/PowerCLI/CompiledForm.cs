namespace PowerCLI;

internal sealed record CompiledForm(
    CommandFormDefinition Definition,
    IReadOnlyList<IReadOnlyList<SyntaxPart>> Branches,
    IReadOnlyDictionary<string, CompiledOption> Options,
    IReadOnlyDictionary<string, CompiledOption> OptionNames);
