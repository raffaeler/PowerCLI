namespace PowerCLI;

public interface ICommandChoiceProvider
{
    ValueTask<CommandChoiceSnapshot> GetChoicesAsync(CommandChoiceContext context, CancellationToken cancellationToken = default);
}
