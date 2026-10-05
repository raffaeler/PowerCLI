using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace PowerCLI.Host;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders()
            .AddFilter(level => level >= LogLevel.Warning)
            .AddSimpleConsole(options => options.ColorBehavior = LoggerColorBehavior.Disabled);
        builder.Services.Configure<ConsoleLoggerOptions>(options => options.LogToStandardErrorThreshold = LogLevel.Warning);
        builder.Services.AddPowerCliDemo();

        using var host = builder.Build();
        return await RunAsync(host);
    }

    public static async Task<int> RunAsync(IHost host, CancellationToken cancellationToken = default)
    {
        var terminal = host.Services.GetRequiredService<TerminalHostedService>();
        await host.RunAsync(cancellationToken);
        return terminal.Failed ? 1 : 0;
    }
}
