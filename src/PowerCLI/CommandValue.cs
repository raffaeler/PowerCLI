namespace PowerCLI;

public sealed record CommandValue(IReadOnlyList<CommandScalar> Items, bool IsSupplied, bool IsDefault = false)
{
    public CommandScalar Scalar => Items.Count == 1 ? Items[0] : throw new InvalidOperationException("Value is not scalar.");
    public IReadOnlyList<string> Strings => Items.Select(item => item.Text).ToArray();
    public string String => Scalar.Text;
    public long Integer => Scalar.Integer ?? throw new InvalidOperationException("Value is not an integer.");
    public double Number => Scalar.Number ?? Scalar.Integer ?? throw new InvalidOperationException("Value is not numeric.");
    public bool Boolean => Scalar.Boolean ?? throw new InvalidOperationException("Value is not boolean.");
}
