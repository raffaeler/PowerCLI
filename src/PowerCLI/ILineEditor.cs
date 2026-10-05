namespace PowerCLI;

public interface ILineEditor
{
    ValueTask<string?> ReadLineAsync(string prompt, CancellationToken cancellationToken = default);
}
