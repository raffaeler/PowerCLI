namespace PowerCLI;

internal sealed record CompiledOption(string Name, CommandOptionDefinition Definition, bool Required, bool Repeated);
