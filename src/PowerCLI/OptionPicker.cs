namespace PowerCLI;

public sealed record OptionPicker(
    string InputPrefix,
    IReadOnlyList<TerminalOption> Options,
    OptionPickerMode Mode = OptionPickerMode.Single,
    int ViewportHeight = 6,
    int? ReplacementStart = null,
    int? ReplacementLength = null,
    string? Filter = null,
    bool AppendSpace = false,
    string? Error = null,
    string? RepeatedOptionName = null);
