namespace PowerCLI;

/// <summary>Runs a terminal loop with configured commands and optional host-defined input processing.</summary>
public sealed class TerminalClientService
{
    private readonly ITerminalConsole _console;
    private readonly ICommandDispatcher _commands;
    private readonly ITerminalInputHandler? _inputHandler;
    private readonly ILineEditor _lineEditor;
    private readonly TerminalClientOptions _options;

    public TerminalClientService(
        ITerminalConsole console,
        ICommandDispatcher commands,
        ITerminalInputHandler? inputHandler = null,
        ILineEditor? lineEditor = null,
        IOptionPickerResolver? completionResolver = null,
        TerminalClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(commands);
        _console = console;
        _commands = commands;
        _inputHandler = inputHandler;
        _lineEditor = lineEditor ?? new InteractiveLineEditor(console, completionResolver);
        _options = options ?? new TerminalClientOptions();
        ArgumentNullException.ThrowIfNull(_options.Prompt);
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_options.WelcomeMessage is { } welcome)
        {
            _console.WriteLine(welcome);
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var input = await _lineEditor.ReadLineAsync(_options.Prompt, cancellationToken);
            if (input is null) break;
            if (string.IsNullOrWhiteSpace(input)) continue;

            var command = await _commands.HandleAsync(input, cancellationToken);
            if (command.IsHandled)
            {
                foreach (var message in command.Messages) _console.WriteLine(message);
                if (command.ClearScreen && !_console.IsOutputRedirected) _console.Clear();
                if (command.ExitRequested) break;
                if (command.Output is { } output)
                {
                    await RenderAsync(output, cancellationToken);
                }
            }
            else if (_inputHandler is { } inputHandler)
            {
                await HandleInputAsync(inputHandler, input, cancellationToken);
            }
            else
            {
                _console.WriteLine("No input handler is configured. Type /help for available commands.");
            }
        }
    }

    private async Task HandleInputAsync(ITerminalInputHandler inputHandler, string input, CancellationToken cancellationToken)
    {
        try
        {
            await RenderAsync(inputHandler.HandleAsync(input, cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _console.WriteLine($"Input failed: {CommandRegistry.Safe(exception.Message)}");
        }
    }

    private async Task RenderAsync(IAsyncEnumerable<TerminalOutput> output, CancellationToken cancellationToken)
    {
        var writer = new ConsoleStreamWriter(_console);
        string? activePrefix = null;
        TerminalTextStyle activeStyle = TerminalTextStyle.Plain;
        var streaming = false;
        try
        {
            await foreach (var item in output.WithCancellation(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item is null || item.Content is null)
                {
                    throw new InvalidOperationException("A host returned a null terminal output.");
                }

                if (item.Kind == TerminalOutputKind.Markdown)
                {
                    if (!streaming || activePrefix != item.Prefix || activeStyle != item.Style)
                    {
                        EndStream();
                        if (item.Prefix is { } prefix)
                            _console.Write(prefix, _console.IsOutputRedirected ? TerminalTextStyle.Plain : item.Style);
                        streaming = true;
                        activePrefix = item.Prefix;
                        activeStyle = item.Style;
                    }
                    writer.WriteDelta(item.Content);
                }
                else if (item.Kind == TerminalOutputKind.Text)
                {
                    EndStream();
                    _console.Write((item.Prefix ?? "") + item.Content,
                        _console.IsOutputRedirected ? TerminalTextStyle.Plain : item.Style);
                    _console.WriteLine();
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported terminal output kind '{item.Kind}'.");
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            EndStream();
            _console.WriteLine($"Output failed: {CommandRegistry.Safe(exception.Message)}");
        }
        finally
        {
            EndStream();
        }

        void EndStream()
        {
            writer.Complete();
            if (streaming)
            {
                _console.WriteLine();
                writer = new ConsoleStreamWriter(_console);
            }
            streaming = false;
        }
    }
}
