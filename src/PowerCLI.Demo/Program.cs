using PowerCLI;
using PowerCLI.Demo;

var console = new SystemTerminalConsole();
var app = new DemoApplication();
var registry = app.CreateCommands();
var service = new TerminalClientService(
    console,
    new TerminalCommandHandler(registry),
    app,
    completionResolver: new TerminalCompletionResolver(registry),
    options: new TerminalClientOptions
    {
        Prompt = "> ",
        WelcomeMessage = "Type /help for commands."
    });

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
try
{
    await service.RunAsync(cancellation.Token);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    console.WriteLine("Cancelled.");
}
