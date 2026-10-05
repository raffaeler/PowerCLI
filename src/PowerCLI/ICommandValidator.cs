namespace PowerCLI;

public interface ICommandValidator
{
    // A null result means success; a nonempty message explains invalid domain input.
    ValueTask<string?> ValidateAsync(CommandInvocation invocation, string name, CommandValue value, CancellationToken cancellationToken = default);
}
