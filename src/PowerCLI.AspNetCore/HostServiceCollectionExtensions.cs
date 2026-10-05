using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace PowerCLI.AspNetCore;

public static class HostServiceCollectionExtensions
{
    /// <summary>Registers a hosted terminal with overridable defaults and optional host shutdown on terminal exit.</summary>
    public static IServiceCollection AddPowerCli(this IServiceCollection services,
        TerminalClientOptions? options = null, bool stopApplicationOnExit = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ITerminalConsole, SystemTerminalConsole>();
        services.TryAddSingleton(_ => new CommandRegistryBuilder().Build());
        services.TryAddSingleton<ICommandDispatcher, TerminalCommandHandler>();
        services.TryAddSingleton<IOptionPickerResolver, TerminalCompletionResolver>();
        services.TryAddSingleton<ILineEditor, InteractiveLineEditor>();
        services.TryAddSingleton(options ?? new TerminalClientOptions());
        services.TryAddSingleton<TerminalClientService>();
        services.TryAddSingleton(provider => new TerminalHostedService(
            provider.GetRequiredService<TerminalClientService>(),
            provider.GetRequiredService<IHostApplicationLifetime>(),
            stopApplicationOnExit));
        services.AddHostedService(provider => provider.GetRequiredService<TerminalHostedService>());
        return services;
    }
}
