using PowerCLI;

namespace PowerCLI.Tests;

internal sealed class RecordingCommandHandler : ICommandHandler
{
    internal List<CommandInvocation> Invocations { get; } = [];
    internal TerminalCommandResult Result { get; set; } = TerminalCommandResult.Message("executed");
    internal Exception? Failure { get; set; }
    public ValueTask<TerminalCommandResult> ExecuteAsync(CommandInvocation invocation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Failure is not null) throw Failure;
        Invocations.Add(invocation);
        return ValueTask.FromResult(Result);
    }
}
