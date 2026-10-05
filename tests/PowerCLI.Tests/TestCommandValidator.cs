using PowerCLI;

namespace PowerCLI.Tests;

internal sealed class TestCommandValidator : ICommandValidator
{
    internal string? Error { get; set; }
    internal CommandInvocation? Invocation { get; private set; }
    public ValueTask<string?> ValidateAsync(CommandInvocation invocation, string name, CommandValue value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Invocation = invocation;
        return ValueTask.FromResult(Error);
    }
}
