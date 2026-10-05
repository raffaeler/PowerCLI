namespace PowerCLI;

internal sealed record SyntaxPart(string Name, bool IsCapture = false, int Minimum = 1, bool Repeated = false);
