# Configurable commands (schema version 1)

PowerCLI automatically registers **only `/help [<command>]`**. All application
commands are explicit host configuration. There is no legacy preset, file
discovery, scripting, reflection-based activation, or runtime reload.

## Fluent C# registration

Use `CommandRegistryBuilder` to attach callbacks without handler/provider/validator
classes or registration dictionaries. The builder creates the same version-1
configuration records and compiles them through `CommandRegistry`; parsing, help,
completion, validation, and diagnostics are shared with JSON hosts.

```csharp
var registry = new CommandRegistryBuilder()
    .Command("/export", command => command
        .Description("Describe a sample export.")
        .Alias("/save")
        .Form("<target>", form => form
            .Argument("target", value => value.Type("relativePath").Description("Sample destination."))
            .Option("--format", "format", option => option
                .Alias("-f").Choices("text", "json").Default("text"))
            .Flag("--force", option => option.Alias("-y"))
            .Option("--tag", "tags", option => option.Repeat())
            .Example("/export sample.json -f json --tag bogus")
            .Handle(input => TerminalCommandResult.Message(
                $"Sample: {input["target"].String}, {input["--format"].String}"))))
    .Build();
```

Add a command with `.Command(name, configure)`, then one or more
`.Form(positionalSyntax, configure)` declarations. Positional syntax uses the existing
literal/capture grammar below, including distinguishable subcommands and alternatives.
Every capture needs `.Argument(name, configure)` (omit `configure` for plain strings).
The form's syntax excludes named options: declare each option once using `.Flag(...)`
or `.Option(name, valueName, configure)`. Their canonical names, value names, aliases,
requiredness, and repetition generate the usage syntax automatically.

| Declaration | Generated syntax |
| --- | --- |
| `.Flag("--force")` | `[--force]` |
| `.Flag("--force", option => option.Required())` | `--force` |
| `.Option("--format", "format")` | `[--format <format>]` |
| `.Option("--format", "format", option => option.Required())` | `--format <format>` |
| `.Option("--tag", "tags", option => option.Repeat())` | `[--tag <tags>]*` |
| `.Option("--tag", "tags", option => option.Required().Repeat())` | `(--tag <tags>)+` |

Flags have Boolean type and cannot repeat. Options default to string type; `.Type(...)`
uses the supported types below. Value builders support `.Description(...)`,
`.Default(string)`, `.Bounds(min, max)`, `.Length(min, max)`, and `.Choices(...)`.
Static choices are closed and case-insensitive by default; `.Suggestions()` allows
other input and `.CaseSensitive()` changes static matching. `.Multiple()` enables
multi-select choices for a repeated capture or option.

`.Handle(...)` accepts `Func<CommandInvocation, TerminalCommandResult>` or
`Func<CommandInvocation, CancellationToken, ValueTask<TerminalCommandResult>>`.
Attach a method group or use an inline callback; no form-ID dispatch switch is needed.
Dynamic choices and validators also accept inline, cancellation-aware callbacks:

```csharp
var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var registry = new CommandRegistryBuilder()
    .Command("/choose", command => command
        .Form("<items>*", form => form
            .Argument("items", value => value.Multiple().Choices((context, token) =>
            {
                token.ThrowIfCancellationRequested();
                return ValueTask.FromResult(new CommandChoiceSnapshot(
                    new[] { "brief", "with examples" }.Select(item =>
                        new TerminalOption(item, item, selected.Contains(item))).ToArray(),
                    Mode: OptionPickerMode.Multiple));
            }))
            .Handle(input =>
            {
                selected.Clear();
                selected.UnionWith(input["items"].Strings);
                return TerminalCommandResult.Message("Updated choices.");
            })))
    .Build();
```

Dynamic callbacks return their matching policy and selection mode in the snapshot;
the mode must agree with `.Multiple()`. They are re-evaluated at execution, not cached
from completion. `.Validate((invocation, name, value, token) => ...)` accepts a
`ValueTask<string?>`: null means valid, and a message explains invalid input.
It sees the same converted/canonical values as interface-based validators.

Generated registration/form IDs are internal plumbing; application callbacks do not
need to name or dispatch on them. Duplicate declarations and invalid configurations
fail explicitly. `Build()` snapshots the declarations; later changes to a retained
builder do not alter an existing registry. Callbacks can intentionally observe mutable
application state, as the selection example does. `/help` remains the only automatic
command; there is no reflection, automatic method discovery, or parameter binding.

## JSON and interface-based host registration

Load a string with `CommandConfiguration.FromJson`, read a stream with
`FromJsonAsync`, or construct the same public records in code. Register opaque
handler, provider, and validator IDs using public interfaces:

```csharp
var configuration = CommandConfiguration.FromJson(json);
var registry = new CommandRegistry(
    configuration,
    new Dictionary<string, ICommandHandler> { ["host.export"] = exportHandler },
    new Dictionary<string, ICommandChoiceProvider> { ["host.targets"] = targetProvider },
    new Dictionary<string, ICommandValidator> { ["host.target"] = targetValidator });
ICommandDispatcher dispatcher = new TerminalCommandHandler(registry);
IOptionPickerResolver completion = new TerminalCompletionResolver(registry);
```

Pass `dispatcher` to `TerminalClientService` and `completion` through its
`completionResolver` parameter, or inject an `ILineEditor` constructed with the
resolver. Neither the terminal loop nor dispatcher has a catalog, selection,
agent, tool, model, or workflow dependency. Its `HandleAsync` method returns `NotACommand` for ordinary
prompts and handled errors for unknown slash commands, including leading-space
input. A host handler implements `ICommandHandler.ExecuteAsync`.

The registry copies configuration and registration dictionaries at construction.
Configuration errors throw `CommandConfigurationException` before input is
accepted. IDs resolve solely against the supplied registries; they are not
executable paths or CLR type names.

## Generic terminal hosting

The core supplies terminal mechanics only. It has no document-kind enum,
catalog, application session, agent interface, workflow runner, model settings,
or built-in tools. Domain types and policies are owned by the host.

```csharp
var terminal = new TerminalClientService(
    console,
    dispatcher,
    inputHandler: hostInputHandler,
    completionResolver: completion,
    options: new TerminalClientOptions
    {
        Prompt = "App> ",
        WelcomeMessage = "Application ready."
    });
await terminal.RunAsync(cancellationToken);
```

The optional `ITerminalInputHandler.HandleAsync(string, CancellationToken)`
returns `IAsyncEnumerable<TerminalOutput>` for ordinary input. Omit it for a
command-only terminal; ordinary input then produces an explicit diagnostic.
The host owns input routing and state. A command can return a generic stream
through `TerminalCommandResult.Output`, without the terminal knowing which
application action produces it.

`TerminalOutputKind.Text` writes a plain line. `Markdown` supplies incremental
fragments processed by the existing renderer. The optional `Prefix` identifies
and labels a stream; changes of prefix/style, a plain line, or stream completion
flush the Markdown stream and terminate its line. `Style` styles the prefix or
plain text through the console adapter. There are no hard-coded answer,
reasoning, or tool activity kinds or labels. Streaming failures are reported,
and cancellation propagates. Set `WelcomeMessage` to null to suppress it.

For dependency-injection wiring, see `src\PowerCLI.Host`. This console sample uses
`Microsoft.Extensions.Hosting` and a `BackgroundService` to run the same
`DemoApplication` as the manually wired demo. One singleton application and registry
serve execution, completion, and ordinary input. The host owns cancellation and
stops when the terminal returns on `/exit` or EOF. Run it with
`dotnet run --project src\PowerCLI.Host`. Hosting dependencies stay in the sample;
the core needs no DI or ASP.NET Core dependency.

## JSON

Associate [commands.schema.json](commands.schema.json) with your configuration in
your editor. The schema describes structural constraints; registry compilation
also checks grammar, determinism, aliases, references, bounds, defaults, and
metadata use. JSON property names are case-sensitive. Unsupported versions,
unknown properties (including nested properties), duplicate properties, missing
required properties, and explicit JSON nulls are errors. Omit optional properties
instead of assigning null.

```json
{
  "schemaVersion": 1,
  "commands": [
    {
      "name": "/export",
      "aliases": ["/save"],
      "description": "Export sample output.",
      "forms": [
        {
          "id": "export",
          "syntax": "<target> [--format <format>] [--force] [--tag <tags>]*",
          "handler": "host.export",
          "arguments": {
            "target": { "type": "relativePath", "description": "Relative destination." }
          },
          "options": {
            "--format": {
              "aliases": ["-f"],
              "valueName": "format",
              "default": "text",
              "choices": { "values": ["text", "json"], "validation": "closed" }
            },
            "--force": { "type": "boolean" },
            "--tag": { "valueName": "tags" }
          },
          "examples": ["/export sample.json -f json --tag bogus"]
        }
      ]
    }
  ]
}
```

Syntax is the suffix **after** the root command. Every form has a unique `id`
within its command and a registered `handler`. Capture definitions belong in
`arguments`; option definitions belong in `options`. `valueName` must match its
option capture and cannot collide with another capture. Bound option values are
indexed by the canonical **option name**, e.g. `invocation["--format"]`, not by
`valueName`. Every definition must be used, and every capture/option must have
exactly one definition.

## Supported grammar and deterministic boundaries

| Syntax | Meaning |
| --- | --- |
| `status` | Required literal keyword. Bare words are not arguments. |
| `<name>` | One scalar positional capture. |
| `(agent \| workflow)` | Required literal-distinguished alternative. |
| `[with <context>]` | Optional sequence; `[a \| b]` is an optional choice. |
| `<ids>*`, `<ids>+` | Zero-or-more / one-or-more trailing positional captures. |
| `--force`, `[--force]` | Required / optional boolean switch. |
| `--format <format>`, `[--format <format>]` | Required / optional valued option. |
| `[--tag <tags>]*`, `(--tag <tags>)+` | Optional / required repeatable valued option. |

Sequences and grouped alternatives may nest, but alternative branches must
start with distinguishable literal keywords. Command roots, aliases, literal
keywords, and option names match case-insensitively. Free text retains its case.
Root names use `/` followed by an ASCII letter and letters/digits/hyphens.
Capture names use an ASCII letter followed by letters/digits/underscores.
Literals use ASCII letters/digits followed by letters/digits/underscores/dots/hyphens.

The compiler expands at most **256 positional branches per form** and rejects
overlapping branches, including overlap between forms. Types, choice contents,
required options, and registration order never disambiguate forms. Shared
prefixes must agree on literal/capture identity. Optional captured sequences
must be trailing, or distinguishable by literal prefixes; for example,
`<source> [with <context>]` and `[brief] detailed` work, whereas
`[<source>] <target>` and `[with <context>] <target>` are rejected.
Repeated positional captures must be the final positional part of every branch.
Repetition of arbitrary fragments, nested options inside positional groups or
alternatives, repeated switches, and empty repeated groups are not supported.
Declare named-option groups at the top level.

Options may occur before, between, or after positional values regardless of
their usage-display position. Supported input is `--name value`,
`--name=value`, and explicitly registered aliases such as `-f value`.
There are no bundled short flags or implicit negative flags. `--` ends option
recognition. Leading-dash positional values require `--`; leading-dash named
values require equals syntax. Nonrepeatable duplicates, including aliases, are
errors. Required repeatable options need at least one occurrence.

Single and double quotes preserve spaces and empty strings. Single quotes are
literal. Inside double quotes, a backslash escapes **the next character**,
including a quote or another backslash. Outside double quotes a backslash is
literal. Execution rejects unclosed quotes and dangling quoted escapes;
completion tolerates them. `CommandLineArguments.QuoteIfNeeded` round-trips
spaces, apostrophes, quotes, backslashes, and empty strings.

## Values, validation, and handler results

Types are `string` (default), signed 64-bit `integer`, finite double `number`,
`boolean` (`true`/`false`), and lexical `relativePath`. Numeric parsing is
invariant, never culture-dependent. Relative paths reject empty/rooted paths,
drive prefixes, and `..` segments on either slash convention. This does not
access the filesystem or protect against symlinks.

Metadata supports `description`, numeric `min`/`max`, string/path
`minLength`/`maxLength`, `default`, `choices`, and a registered custom `validator`.
Bounds are inclusive. Defaults are strings parsed through the declared type and
are permitted only for optional scalar captures or nonrepeatable valued options.
Defaults relying on closed dynamic choices or custom validators are rejected:
they cannot be validated synchronously at startup without invocation context.
Absent switches bind to `false`, supplied switches to `true`. Absent valued
captures/options have empty `Items` unless a default exists.

`CommandInvocation` provides canonical command/form/handler IDs, raw input,
canonical matched literals, immutable bound values, and token source spans.
`CommandValue` exposes `Scalar`, `Items`, `String`, `Strings`, `Integer`, `Number`,
and `Boolean`, plus `IsSupplied` and `IsDefault`. Scalar accessors reject absent
or collection values rather than silently substituting values.

Static choices use `values`; dynamic choices use a registered `provider`, never
both. `validation: "closed"` rejects values outside the current choice snapshot
and binds canonical spelling. `"suggestions"` offers choices without restricting
free input. Static matching defaults to case-insensitive; `caseSensitive` opts
out. Dynamic providers return their matching policy and picker mode in
`CommandChoiceSnapshot`, with labels and selected-item state in `TerminalOption`.
Selection mode must agree with configuration. Multiple selection requires a
repeated choice-backed capture/option; named-option menus insert one canonical
option occurrence per selected value.
Choice values cannot contain terminal control characters; provider labels are
sanitized before they are returned to either menus or completion clients.

Providers receive `CommandChoiceContext` with earlier parsed literals/values.
Execution fetches a **fresh snapshot**, never completion's candidates. Provider
failures are visible; cancellation propagates. Custom validators receive all
converted/canonical values and return null for success or an error message.
Invalid input returns `CommandDiagnostic` source spans and generated usage
without invoking any handler. Host handler failures use the same error-result
pathway. Handlers return `TerminalCommandResult` to supply messages, clear,
exit, and generic streamed output without console I/O in the dispatcher.
Configured handlers must return a handled result; an unhandled/null host result
is a visible error and never routes slash input to the ordinary-input handler.

## Help, editor, and trust

`/help` lists only registered roots and descriptions. `/help use`, `/help /use`,
and aliases show the same forms, argument/option metadata, defaults, descriptions,
and examples. Unknown targets are errors. Help and picker labels strip terminal
control characters.

Completion shares the compiled registry and binder with execution. It offers
registered roots/aliases, literal branches, unused options, static choices, and
dynamic contextual values. `CompletionRequest` carries input and cursor;
`OptionPicker` carries the exact replacement span and cursor-prefix filter.
Accepting a menu preserves the suffix. Multiple selection replaces only its
contiguous capture/option segment and never removes surrounding options.
Space toggles multiple choices, arrows navigate, Escape dismisses, and Enter
accepts then submits on the next Enter; an exact final scalar choice can submit
immediately. Chained menus, six-row scrolling, cursor editing, and quoting remain
available. Redirects use ordinary line input, no cursor operations or injected
ANSI styling. When no choices are displayed, Up/Down browses editor-session history
and Ctrl+R removes the recalled entry. Navigation past the newest entry or removal of
it restores the original draft and cursor. Application-specific authorization and
approval policies belong to the host.

The library does not define or select catalog resources. The two-file demo uses
`Program.cs` for terminal wiring and `DemoApplication.cs` for fluent commands and
sample streamed responses. `/echo` demonstrates quoted input and a flag, `/choose`
demonstrates dynamic multi-selection and selected-item highlighting, and `/export`
demonstrates defaults, aliases, types, and repeated options without file access.
`/clear` and `/exit` return the existing generic command effects. No agents,
workflows, catalog documents, tools, or approval services are required to host a
terminal. A real application owns its domain policies and must revalidate any
authorization-sensitive actions at execution.

### Migrating the original domain-bound API

The old core `TerminalDocumentKind`, catalog/selection types, agent/workflow
interfaces and activities, tool catalog/policies, model preflight, and sample-root
startup settings are removed. The demo no longer carries that domain framework;
applications retain their own domain logic outside the terminal library.
`WorkflowToRun` is removed from command results. Supply a host-produced
`Output` stream instead. Replace the old service constructor's selection,
agent, workflow runner, tools, and approval parameters with a single optional
`ITerminalInputHandler`. This is an intentional breaking API correction.
