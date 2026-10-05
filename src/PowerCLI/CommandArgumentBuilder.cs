namespace PowerCLI;

/// <summary>Configures a capture declared in a form's positional syntax.</summary>
public sealed class CommandArgumentBuilder : CommandValueBuilder<CommandArgumentBuilder>
{
    internal CommandArgumentBuilder(string location) : base(location) { }
    protected override CommandArgumentBuilder Self => this;
}
