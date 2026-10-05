namespace PowerCLI;

internal sealed record CompiledCommand(CommandDefinition Definition, IReadOnlyList<CompiledForm> Forms);
