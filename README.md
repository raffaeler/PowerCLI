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
| `src/PowerCLI.Demo` | Two-file sample application with fluent commands, multi-select choices, and ordinary-input streaming. |
| `tests/PowerCLI.Tests` | xUnit v3 coverage for commands, completion, history, Markdown rendering, argument parsing, and the sample app. |

Run the sample with `dotnet run --project src\PowerCLI.Demo` and verify the solution with `dotnet test PowerCLI.sln`.

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
selected set. **Escape** closes the picker without changing the input.
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
