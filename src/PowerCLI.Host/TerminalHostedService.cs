using Microsoft.Extensions.Hosting;

namespace PowerCLI.Host;

public sealed class TerminalHostedService(
    TerminalClientService terminal,
    IHostApplicationLifetime lifetime) : BackgroundService
{
    private readonly TerminalClientService _terminal = terminal;
    private readonly IHostApplicationLifetime _lifetime = lifetime;

    public bool Failed { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _terminal.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            Failed = true;
            throw;
        }

        _lifetime.StopApplication();
    }
}
