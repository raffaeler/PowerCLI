namespace PowerCLI;

public sealed record CommandChoiceSnapshot(IReadOnlyList<TerminalOption> Options, bool CaseSensitive = false, OptionPickerMode Mode = OptionPickerMode.Single);
