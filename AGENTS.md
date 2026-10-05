# PowerCLI contributor requirements

- Target .NET 10 (`net10.0`) and retain nullable reference types and implicit usings.
- Always name private fields with a leading underscore followed by camelCase (for example, `_terminal`).
- Always declare exactly one type per file.
- Use `xunit.v3` for all code tests.
- Keep `src/PowerCLI` generic and console-independent. Inject terminal I/O, command dispatch, completion, and ordinary-input handling through public interfaces. Catalog/document kinds, application selections, agents, workflows, model settings, and tools belong to the host or demo, never the core.
- Preserve redirected-console behavior: never emit ANSI styling to redirected output and never request an interactive approval from redirected input.
- In the demo, catalog-visible documents must be trusted before they may be selected or printed. Skills and enabled instructions are the only selectable supporting documents.
- Keep command parsing case-insensitive and quote-aware. Add or update xUnit v3 tests for command, completion, renderer, selection, or tool behavior changes.
- Interactive input must use raw-key editing and contextual menus, never numbered prompts. Up/Down navigates, Space toggles multiple choices, Enter accepts a choice (then submits on the next Enter), and Escape dismisses the menu. Preserve filtering, chained argument menus, selected-item highlighting, scrolling, cursor editing, quoted values, and cancellation.
- Run `dotnet test PowerCLI.sln` after functional changes. The `PowerCLI.Demo` project must stay dependency-free apart from the library and use bogus/sample data only.
