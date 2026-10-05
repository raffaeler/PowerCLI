namespace PowerCLI;

internal sealed record CommandBinding(
    CommandInvocation Invocation,
    SyntaxPart? Next,
    CompiledOption? PendingOption,
    IReadOnlySet<string> UsedOptions,
    bool OptionsEnded);
