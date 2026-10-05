namespace PowerCLI;

internal static class SyntaxPartExtensions
{
    internal static int IndexOfPart(this IReadOnlyList<SyntaxPart> parts, SyntaxPart part)
    {
        for (var index = 0; index < parts.Count; index++) if (parts[index] == part) return index;
        return -1;
    }
}
