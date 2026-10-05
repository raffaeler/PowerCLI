using Microsoft.Extensions.Hosting;

namespace PowerCLI.AspNetCore;

public sealed class TerminalHostedService(
    TerminalClientService terminal,
    IHostApplicationLifetime lifetime,
    bool stopApplicationOnExit = true) : BackgroundService
{
    private readonly TerminalClientService _terminal = terminal;
    private readonly IHostApplicationLifetime _lifetime = lifetime;
    private readonly bool _stopApplicationOnExit = stopApplicationOnExit;

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

        if (_stopApplicationOnExit) _lifetime.StopApplication();
    }
}
