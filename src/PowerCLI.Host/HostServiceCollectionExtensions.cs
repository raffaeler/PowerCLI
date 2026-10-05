using Microsoft.Extensions.DependencyInjection;
using PowerCLI.Demo;

namespace PowerCLI.Host;

public static class HostServiceCollectionExtensions
{
    public static IServiceCollection AddPowerCliDemo(this IServiceCollection services)
    {
        services.AddSingleton<ITerminalConsole, SystemTerminalConsole>();
        services.AddSingleton<DemoApplication>();
        services.AddSingleton<ITerminalInputHandler>(provider => provider.GetRequiredService<DemoApplication>());
        services.AddSingleton(provider => provider.GetRequiredService<DemoApplication>().CreateCommands());
        services.AddSingleton<ICommandDispatcher, TerminalCommandHandler>();
        services.AddSingleton<IOptionPickerResolver, TerminalCompletionResolver>();
        services.AddSingleton<ILineEditor, InteractiveLineEditor>();
        services.AddSingleton(new TerminalClientOptions { Prompt = "> ", WelcomeMessage = "Type /help for commands." });
        services.AddSingleton<TerminalClientService>();
        services.AddSingleton<TerminalHostedService>();
        services.AddHostedService(provider => provider.GetRequiredService<TerminalHostedService>());
        return services;
    }
}
