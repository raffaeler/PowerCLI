extern alias PowerCliHost;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PowerCLI;
using Xunit;
using HostApplication = PowerCliHost::PowerCLI.Demo.DemoApplication;
using HostProgram = PowerCliHost::PowerCLI.Host.Program;
using HostServices = PowerCliHost::PowerCLI.Host.HostServiceCollectionExtensions;
using HostedTerminal = PowerCliHost::PowerCLI.Host.TerminalHostedService;

namespace PowerCLI.Tests;

public sealed class HostedTerminalTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static TimeSpan Timeout => TimeSpan.FromSeconds(10);

    [Fact]
    public void RegistrationsResolveOneSharedSingletonGraph()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostApplicationLifetime>(new ApplicationLifetimeStub());
        HostServices.AddPowerCliDemo(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.IsType<SystemTerminalConsole>(provider.GetRequiredService<ITerminalConsole>());
        Assert.IsType<TerminalCommandHandler>(provider.GetRequiredService<ICommandDispatcher>());
        Assert.IsType<TerminalCompletionResolver>(provider.GetRequiredService<IOptionPickerResolver>());
        Assert.IsType<InteractiveLineEditor>(provider.GetRequiredService<ILineEditor>());
        Assert.Same(provider.GetRequiredService<HostApplication>(), provider.GetRequiredService<ITerminalInputHandler>());
        Assert.Same(provider.GetRequiredService<HostedTerminal>(), Assert.Single(provider.GetServices<IHostedService>()));
        Assert.Same(provider.GetRequiredService<TerminalClientService>(), provider.GetRequiredService<TerminalClientService>());
        Assert.Same(provider.GetRequiredService<CommandRegistry>(), provider.GetRequiredService<CommandRegistry>());
        Assert.Equal(["/help", "/echo", "/choose", "/export", "/clear", "/exit"],
            provider.GetRequiredService<CommandRegistry>().CommandNames);
        Assert.Equal("> ", provider.GetRequiredService<TerminalClientOptions>().Prompt);
        Assert.Equal("Type /help for commands.", provider.GetRequiredService<TerminalClientOptions>().WelcomeMessage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExitOrEofStopsTheHostSuccessfully(bool exitCommand)
    {
        var console = new FakeConsole(true, true);
        if (exitCommand) console.Lines.Enqueue("/exit");
        var logs = new RecordingLogProvider();
        using var host = CreateBuilder(console, logs).Build();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        var worker = host.Services.GetRequiredService<HostedTerminal>();

        Assert.Equal(0, await HostProgram.RunAsync(host, Token).WaitAsync(Timeout, Token));

        Assert.True(lifetime.ApplicationStopping.IsCancellationRequested);
        Assert.True(lifetime.ApplicationStopped.IsCancellationRequested);
        Assert.False(worker.Failed);
        Assert.Equal(exitCommand, console.Output.ToString().Contains("Goodbye.", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Entries, entry => entry.Level >= LogLevel.Error);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task HostedDemoPreservesCommandsStateStreamingAndRedirects(bool inputRedirected, bool outputRedirected)
    {
        var console = new FakeConsole(inputRedirected, outputRedirected);
        foreach (var input in new[] { "/clear", "/help", "/ECHO \"hello world\" --UPPER", "/choose \"with examples\" friendly",
                     "/export sample.json -f JSON -y --tag bogus", "ordinary", "/exit" })
            console.Lines.Enqueue(input);
        var surface = new FakeSurface([]);
        var builder = CreateBuilder(console, new RecordingLogProvider());
        builder.Services.AddSingleton<ILineEditor>(provider =>
            new InteractiveLineEditor(console, provider.GetRequiredService<IOptionPickerResolver>(), surface));
        using var host = builder.Build();
        var app = host.Services.GetRequiredService<HostApplication>();
        var resolver = host.Services.GetRequiredService<IOptionPickerResolver>();

        Assert.Equal(0, await HostProgram.RunAsync(host, Token).WaitAsync(Timeout, Token));

        var picker = await resolver.ResolvePickerAsync("/choose ", Token);
        Assert.NotNull(picker);
        Assert.Equal(["with examples", "friendly"], app.SelectedItems);
        Assert.Equal(["with examples", "friendly"], picker.Options.Where(option => option.IsSelected).Select(option => option.Value));
        var output = console.Output.ToString();
        Assert.Contains("/echo", output);
        Assert.Contains("HELLO WORLD", output);
        Assert.Contains("format=json, force=True, tags=bogus.", output);
        Assert.Contains("Demo> Sample response: ordinary", output);
        Assert.Contains("Selected: with examples, friendly", output);
        Assert.Contains("Goodbye.", output);
        Assert.Empty(surface.Output.ToString());
        Assert.DoesNotContain('\u001b', output);
        Assert.Equal(outputRedirected ? 0 : 1, console.Clears);
        if (outputRedirected) Assert.All(console.Styles, style => Assert.Equal(TerminalTextStyle.Plain, style));
    }

    [Fact]
    public async Task HostStopCancelsPendingInteractiveReadWithoutFailure()
    {
        var console = new FakeConsole();
        var surface = new BlockingSurface();
        var logs = new RecordingLogProvider();
        var builder = CreateBuilder(console, logs);
        builder.Services.AddSingleton<ILineEditor>(provider =>
            new InteractiveLineEditor(console, provider.GetRequiredService<IOptionPickerResolver>(), surface));
        using var host = builder.Build();
        var worker = host.Services.GetRequiredService<HostedTerminal>();

        await host.StartAsync(Token).WaitAsync(Timeout, Token);
        await surface.ReadStarted.Task.WaitAsync(Timeout, Token);
        await host.StopAsync(Token).WaitAsync(Timeout, Token);
        await worker.ExecuteTask!.WaitAsync(Timeout, Token);

        Assert.False(worker.Failed);
        Assert.DoesNotContain(logs.Entries, entry => entry.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task RunCancellationStopsTheHostAndReturnsSuccess()
    {
        var console = new FakeConsole();
        var surface = new BlockingSurface();
        var logs = new RecordingLogProvider();
        var builder = CreateBuilder(console, logs);
        builder.Services.AddSingleton<ILineEditor>(provider =>
            new InteractiveLineEditor(console, provider.GetRequiredService<IOptionPickerResolver>(), surface));
        using var host = builder.Build();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var run = HostProgram.RunAsync(host, cancellation.Token);

        await surface.ReadStarted.Task.WaitAsync(Timeout, Token);
        cancellation.Cancel();

        Assert.Equal(0, await run.WaitAsync(Timeout, Token));
        Assert.True(lifetime.ApplicationStopped.IsCancellationRequested);
        Assert.DoesNotContain(logs.Entries, entry => entry.Level >= LogLevel.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnexpectedReadFailureIsLoggedAndReturnsFailureExitCode(bool unrelatedCancellation)
    {
        Exception failure = unrelatedCancellation ? new OperationCanceledException("Unrelated cancellation.") :
            new InvalidOperationException("Terminal read failed.");
        var logs = new RecordingLogProvider();
        var builder = CreateBuilder(new FakeConsole(true, true), logs);
        builder.Services.AddSingleton<ILineEditor>(new ThrowingLineEditor(failure));
        using var host = builder.Build();
        var worker = host.Services.GetRequiredService<HostedTerminal>();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

        Assert.Equal(1, await HostProgram.RunAsync(host, Token).WaitAsync(Timeout, Token));

        Assert.True(worker.Failed);
        Assert.True(lifetime.ApplicationStopped.IsCancellationRequested);
        Assert.Contains(logs.Entries, entry => entry.Level >= LogLevel.Error && ReferenceEquals(entry.Exception, failure));
    }

    private static HostApplicationBuilder CreateBuilder(FakeConsole console, RecordingLogProvider logs)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true
        });
        builder.Logging.ClearProviders().AddProvider(logs);
        HostServices.AddPowerCliDemo(builder.Services);
        builder.Services.AddSingleton<ITerminalConsole>(console);
        return builder;
    }
}
