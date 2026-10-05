namespace PowerCLI;

public sealed record TerminalOption(string Value, string Label, bool IsSelected = false, bool QuoteWhenInserted = true);
