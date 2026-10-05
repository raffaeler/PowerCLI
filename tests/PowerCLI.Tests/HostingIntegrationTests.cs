using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PowerCLI;
using PowerCLI.AspNetCore;
using Xunit;

namespace PowerCLI.Tests;

public sealed class HostingIntegrationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static TimeSpan Timeout => TimeSpan.FromSeconds(10);

    [Fact]
    public void DefaultsResolveWithoutDemoOrOrdinaryInputHandler()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostApplicationLifetime>(new ApplicationLifetimeStub());

        Assert.Same(services, services.AddPowerCli());
        services.AddPowerCli();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.IsType<SystemTerminalConsole>(provider.GetRequiredService<ITerminalConsole>());
        Assert.IsType<TerminalCommandHandler>(provider.GetRequiredService<ICommandDispatcher>());
        Assert.IsType<TerminalCompletionResolver>(provider.GetRequiredService<IOptionPickerResolver>());
        Assert.IsType<InteractiveLineEditor>(provider.GetRequiredService<ILineEditor>());
        Assert.Null(provider.GetService<ITerminalInputHandler>());
        Assert.Equal(["/help"], provider.GetRequiredService<CommandRegistry>().CommandNames);
        Assert.Equal(new TerminalClientOptions(), provider.GetRequiredService<TerminalClientOptions>());
        Assert.Same(provider.GetRequiredService<TerminalHostedService>(), Assert.Single(provider.GetServices<IHostedService>()));
        Assert.Same(provider.GetRequiredService<TerminalClientService>(), provider.GetRequiredService<TerminalClientService>());
        Assert.DoesNotContain(typeof(TerminalHostedService).Assembly.GetReferencedAssemblies(),
            reference => reference.Name is "PowerCLI.Demo" or "PowerCLI.Host");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomServicesRegisteredBeforeOrAfterDefaultsAreUsed(bool registerBefore)
    {
        var console = new FakeConsole(true, true);
        console.Lines.Enqueue("ordinary");
        console.Lines.Enqueue("/exit");
        var registry = new CommandRegistryBuilder().Build();
        var dispatcher = new RecordingDispatcher();
        var inputHandler = new RecordingInputHandler { Output = [new("Custom response.")] };
        var resolver = new FixedResolver(null);
        var editor = new InteractiveLineEditor(console, resolver);
        var options = new TerminalClientOptions { Prompt = "Custom> ", WelcomeMessage = "Custom welcome." };
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        if (registerBefore) RegisterCustomServices();
        builder.Services.AddPowerCli();
        if (!registerBefore) RegisterCustomServices();
        using var host = builder.Build();

        Assert.Same(console, host.Services.GetRequiredService<ITerminalConsole>());
        Assert.Same(registry, host.Services.GetRequiredService<CommandRegistry>());
        Assert.Same(dispatcher, host.Services.GetRequiredService<ICommandDispatcher>());
        Assert.Same(inputHandler, host.Services.GetRequiredService<ITerminalInputHandler>());
        Assert.Same(resolver, host.Services.GetRequiredService<IOptionPickerResolver>());
        Assert.Same(editor, host.Services.GetRequiredService<ILineEditor>());
        Assert.Same(options, host.Services.GetRequiredService<TerminalClientOptions>());
        var worker = host.Services.GetRequiredService<TerminalHostedService>();

        await host.RunAsync(Token).WaitAsync(Timeout, Token);

        Assert.Equal(["ordinary", "/exit"], dispatcher.Inputs);
        Assert.Equal(["ordinary"], inputHandler.Inputs);
        Assert.StartsWith("Custom welcome.", console.Output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Custom> ", console.Output.ToString());
        Assert.Contains("Custom response.", console.Output.ToString());
        Assert.False(worker.Failed);

        void RegisterCustomServices()
        {
            builder.Services.AddSingleton<ITerminalConsole>(console);
            builder.Services.AddSingleton(registry);
            builder.Services.AddSingleton<ICommandDispatcher>(dispatcher);
            builder.Services.AddSingleton<ITerminalInputHandler>(inputHandler);
            builder.Services.AddSingleton<IOptionPickerResolver>(resolver);
            builder.Services.AddSingleton<ILineEditor>(editor);
            builder.Services.AddSingleton(options);
        }
    }

    [Theory]
    [InlineData("Application ready.")]
    [InlineData(null)]
    public async Task OptionsParameterControlsPromptAndOptionalWelcome(string? welcome)
    {
        var console = new FakeConsole(true, true);
        console.Lines.Enqueue("/help");
        var options = new TerminalClientOptions { Prompt = "App> ", WelcomeMessage = welcome };
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddSingleton<ITerminalConsole>(console);
        builder.Services.AddPowerCli(options);
        using var host = builder.Build();

        Assert.Same(options, host.Services.GetRequiredService<TerminalClientOptions>());
        await host.RunAsync(Token).WaitAsync(Timeout, Token);

        var output = console.Output.ToString();
        Assert.Contains("App> ", output);
        Assert.Contains("/help [<command>]", output);
        Assert.DoesNotContain("Type /help for commands.", output);
        if (welcome is not null) Assert.StartsWith(welcome, output, StringComparison.Ordinal);
        else Assert.StartsWith("App> ", output, StringComparison.Ordinal);
        Assert.DoesNotContain('\u001b', output);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TerminalExitCanStopTheHostOrLeaveOtherServicesRunning(bool stopApplicationOnExit, bool exitCommand)
    {
        var console = new FakeConsole(true, true);
        if (exitCommand) console.Lines.Enqueue("/exit");
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddSingleton<ITerminalConsole>(console);
        builder.Services.AddSingleton<ICommandDispatcher>(new RecordingDispatcher());
        builder.Services.AddPowerCli(stopApplicationOnExit: stopApplicationOnExit);
        using var host = builder.Build();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        var worker = host.Services.GetRequiredService<TerminalHostedService>();

        await host.StartAsync(Token).WaitAsync(Timeout, Token);
        await worker.ExecuteTask!.WaitAsync(Timeout, Token);

        Assert.Equal(stopApplicationOnExit, lifetime.ApplicationStopping.IsCancellationRequested);
        Assert.False(worker.Failed);
        await host.StopAsync(Token).WaitAsync(Timeout, Token);
    }
}
