namespace PowerCLI;

public sealed record CommandInvocation(
    string Command,
    string FormId,
    string HandlerId,
    string RawInput,
    IReadOnlyList<string> Literals,
    IReadOnlyDictionary<string, CommandValue> Values,
    IReadOnlyDictionary<string, IReadOnlyList<CommandToken>> Sources)
{
    public CommandValue this[string name] => Values[name];
}
