namespace PowerCLI;

public sealed record CommandDiagnostic(string Message, int Start = 0, int Length = 0, string? Location = null);
