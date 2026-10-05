using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using PowerCLI.AspNetCore;
using PowerCLI.Demo;

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
        ConfigureServices(builder.Services);

        using var host = builder.Build();
        return await RunAsync(host);
    }

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<DemoApplication>();
        services.AddSingleton<ITerminalInputHandler>(provider => provider.GetRequiredService<DemoApplication>());
        services.AddSingleton(provider => provider.GetRequiredService<DemoApplication>().CreateCommands());
        services.AddPowerCli(new TerminalClientOptions { Prompt = "> ", WelcomeMessage = "Type /help for commands." });
    }

    public static async Task<int> RunAsync(IHost host, CancellationToken cancellationToken = default)
    {
        var terminal = host.Services.GetRequiredService<TerminalHostedService>();
        await host.RunAsync(cancellationToken);
        return terminal.Failed ? 1 : 0;
    }
}
