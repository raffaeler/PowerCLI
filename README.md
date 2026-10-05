# PowerCLI

`PowerCLI` is a .NET 10, console-independent class library for building general-purpose interactive terminals. It supplies:

- Fluent C# or version-1 JSON commands, typed quote-aware parsing, generated help, and cursor-aware completion.
- A generic terminal loop with host-defined input processing, streamed output, and configurable prompt text.
- Raw-key editing and filtered single/multi-select menus, ANSI/plain console adapters, and redirected-I/O safeguards.
- Incremental Markdown streaming with headings, code, lists, links, images, emphasis, tables-as-text, and HTML break normalization.

The library has no document kinds, catalog/session model, agents, skills,
instructions, workflow engine, model configuration, or built-in tools. Those
concepts belong to the host application, not the terminal.

## Projects

| Project | Purpose |
| --- | --- |
| `src/PowerCLI` | Reusable command, editing, rendering, and terminal I/O library with no application-domain dependency. |
| `src/PowerCLI.AspNetCore` | Reusable DI registrations and terminal background service for .NET Generic Host and ASP.NET Core applications. |
| `src/PowerCLI.Demo` | Two-file sample application with fluent commands, multi-select choices, and ordinary-input streaming. |
| `src/PowerCLI.Host` | The same sample using dependency injection and a .NET Generic Host background service. |
| `tests/PowerCLI.Tests` | xUnit v3 coverage for commands, completion, history, Markdown rendering, argument parsing, sample apps, and hosted lifecycle. |

Run the sample with `dotnet run --project src\PowerCLI.Demo` and verify the solution with `dotnet test PowerCLI.slnx`.
Run the hosted version with `dotnet run --project src\PowerCLI.Host`.

## NuGet packages

The reusable projects produce `PowerCLI` and `PowerCLI.AspNetCore` packages targeting
`net10.0`. `PowerCLI.AspNetCore` depends on the matching version of `PowerCLI` and
`Microsoft.Extensions.Hosting.Abstractions`; the core has no package dependencies.
The sample applications and test project are not packable.

Build both packages and their portable-PDB symbol packages:

```powershell
dotnet pack PowerCLI.slnx --configuration Release --output artifacts\packages
```

The initial version defaults to `0.1.0`. Override it for a release using
`-p:Version=1.0.0` (or a prerelease such as `-p:Version=1.0.0-preview.1`) on the
same command, so both packages and the project-reference dependency stay aligned.
Shared metadata is in `src\NuGetPackage.props`; each library declares its own
package ID, description, and tags.

Both packages include `Logo.png`, the README, MIT license, and XML API documentation, with
`Raffaele Rialdi (@raffaeler)` as the author. The .NET 10 SDK supplies GitHub
SourceLink support without an additional package reference. The `.snupkg` files
contain portable PDBs mapping tracked source files to the exact Git commit;
untracked compiled sources are embedded. CI builds enable normalized,
deterministic paths when `CI`, `GITHUB_ACTIONS`, or `TF_BUILD` is `true`; for
other build systems, pass `-p:ContinuousIntegrationBuild=true`.

Pack releases from a clean, committed Git checkout so SourceLink retrieves the
same source used to build the assemblies. Before publishing, confirm the package
IDs are available to your NuGet.org account and choose the release version.
Publishing is a separate, explicit step; this repository does not store API keys
or automatically push packages.

**Only `/help` is automatically built in.** The simplest registration path is
`CommandRegistryBuilder`: declare commands and attach ordinary methods or inline callbacks.
Each option is declared once; the builder generates its syntax and metadata together.

```csharp
var registry = new CommandRegistryBuilder()
    .Command("/echo", command => command
        .Description("Echo quoted text.")
        .Form("<text>", form => form
            .Argument("text")
            .Flag("--upper")
            .Handle(input => TerminalCommandResult.Message(
                input["--upper"].Boolean ? input["text"].String.ToUpperInvariant() : input["text"].String))))
    .Build();
```

Add another `.Command(...)` to register an action. Add a `.Flag(...)` or
`.Option("--format", "format", option => option.Choices("text", "json").Default("text"))`
to its form to add an option, without editing a separate syntax string or handler registry.
Callbacks for asynchronous execution, dynamic choices, and custom validation use the
same generic engine. JSON configuration, public records, and explicit interface-based
registration remain supported for hosts that prefer them. Use
`TerminalCommandHandler` as the injected `ICommandDispatcher` and
`TerminalCompletionResolver` as the editor's `IOptionPickerResolver`.
Invalid or ambiguous definitions are rejected before input is accepted; there
is no runtime reload, script execution, reflection activation, or implicit legacy
command preset.

See the [command configuration and grammar reference](docs/commands.md) and
[version-1 JSON Schema](docs/commands.schema.json) for aliases, subcommands,
alternatives, optional/repeated arguments, named options, types/bounds/defaults,
static/dynamic choices, host registration, and deliberate deterministic grammar
limits.

The demo contains only `Program.cs` (terminal wiring and cancellation) and
`DemoApplication.cs` (command declarations, small application callbacks, and streamed
sample responses). It provides `/echo`, `/choose`, `/export`, `/clear`, and `/exit`
alongside built-in `/help`. `/export` only describes a bogus operation; it never writes
files. There are no demo-specific interfaces, agent/workflow/tool framework, external
dependencies, or JSON files to deploy.

`PowerCLI.Host` demonstrates the same terminal with .NET Generic Host dependency
injection instead of manual wiring. It compile-links `DemoApplication.cs`, so both
demos share the sample commands and behavior without referencing the demo executable.
Its sample-specific registrations share one application instance and command
registry across dispatch, completion, and ordinary input. It uses the reusable
`AddPowerCli` extension and `TerminalHostedService` from `PowerCLI.AspNetCore`;
`/exit` or EOF stops the host, and the host
owns Ctrl+C cancellation. Informational host logs are suppressed, while unexpected
background-service failures are logged to stderr without ANSI colors and return
a nonzero exit code. Hosting integration lives in `PowerCLI.AspNetCore`, which
references only the core and hosting abstractions; the core and original demo
remain dependency-free. There is no required ASP.NET Core web server, web framework
reference, or configuration file.

For another host, reference `PowerCLI.AspNetCore` and register your own application
services, then add the terminal:

```csharp
using PowerCLI;
using PowerCLI.AspNetCore;

builder.Services.AddSingleton(commandRegistry);
builder.Services.AddSingleton<ITerminalInputHandler, ApplicationInputHandler>();
builder.Services.AddPowerCli(
    new TerminalClientOptions { Prompt = "App> ", WelcomeMessage = "Application ready." },
    stopApplicationOnExit: false);
```

The input handler is optional. Without a custom registry, only `/help` is available.
Console, dispatch, completion, editor, registry, and terminal options registrations
can be supplied before or after `AddPowerCli`; existing registrations are preserved.
Set `WelcomeMessage = null` to suppress the welcome text. By default, terminal exit
or EOF stops the application; use `stopApplicationOnExit: false` when a web server
or other hosted services must keep running. Host cancellation still stops the
terminal, and unexpected failures propagate to the host's background-service
error handling. Repeated registration adds only one terminal hosted service;
the first call supplies defaults unless explicitly overridden through DI.

`TerminalClientService` requires only a console and command dispatcher. Supply
an optional `ITerminalInputHandler` for ordinary input, returning plain or
incremental Markdown `TerminalOutput` values. Command handlers can return the
same output stream through `TerminalCommandResult.Output`. The host owns all
state, routing, output labels, and application actions; the core never interprets
resource IDs or chooses an agent/workflow. `TerminalClientOptions` controls the
prompt and optional welcome text.

Type `/` to open the command menu. Use **Up/Down** to navigate; typing filters choices.
**Enter** accepts the highlighted option and advances to its argument menu, when applicable.
Press **Enter** again to submit the completed line (an exact single choice submits immediately).
For the demo's `/choose ` menu, **Space** toggles choices and **Enter** accepts the
selected set. **Escape** closes the picker without changing the input; when no picker
is open, it clears the entire input line and resets the caret to the start.
**Left/Right**, **Home/End**, **Backspace**, and **Delete** edit the line.
When no menu choices are shown, **Up/Down** browses submitted input history without wrapping.
Moving down past the newest entry restores your original draft and cursor.
**Ctrl+R** removes the recalled history entry and shows the next newer entry, or restores
the draft if there is none. It does nothing when menu choices are shown or no history
entry is recalled. Recalled lines keep menus closed until you resume editing.
History lasts for the editor instance, includes all non-blank submitted inputs, and
suppresses consecutive exact duplicates. Editing a recalled line does not change its stored
entry; submitting it records the edited input. Standalone pickers do not use history.
Long menus scroll within a six-row viewport. Values containing spaces, quotes, or backslashes
are quoted and escaped automatically. Redirected input or output uses ordinary line input
without cursor movement or menus.
Completing in the middle of a line preserves the surrounding text; option values
support both separated and equals syntax. Execution re-fetches dynamic choices,
rather than treating a completion snapshot as validation or authorization.
Application-specific trust and approval policies remain the host's responsibility.
