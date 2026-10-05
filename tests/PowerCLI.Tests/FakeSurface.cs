using System.Text;
using PowerCLI;

namespace PowerCLI.Tests;

internal class FakeSurface(IEnumerable<ConsoleKeyInfo> keys, int bufferHeight = 40, bool canExpand = true,
    int initialTop = 0, bool virtualCursor = false) : IConsoleInteractionSurface
{
    private readonly Queue<ConsoleKeyInfo> _keys = new(keys);
    private readonly bool _canExpand = canExpand;
    public int RemainingKeys => _keys.Count;
    public int CursorLeft { get; private set; }
    public int CursorTop { get; private set; } = initialTop;
    public int WindowWidth => 120;
    public int BufferHeight { get; private set; } = bufferHeight;
    public bool UsesVirtualCursor { get; } = virtualCursor;
    public StringBuilder Output { get; } = new();
    public List<string> Highlighted { get; } = [];
    public void EnqueueKeys(IEnumerable<ConsoleKeyInfo> keys)
    {
        foreach (var key in keys) _keys.Enqueue(key);
    }
    public bool EnsureBufferHeight(int minimumHeight)
    {
        if (_canExpand) BufferHeight = Math.Max(BufferHeight, minimumHeight);
        return minimumHeight <= BufferHeight;
    }
    public virtual ValueTask<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_keys.Dequeue());
    public void SetCursorPosition(int left, int top)
    {
        if (left < 0 || left >= WindowWidth || top < 0 || (!UsesVirtualCursor && top >= BufferHeight))
            throw new ArgumentOutOfRangeException(nameof(top), $"Invalid cursor ({left}, {top}).");
        CursorLeft = left;
        CursorTop = top;
    }
    public void Write(string value, bool selected = false)
    {
        Output.Append(value);
        CursorLeft += value.Length;
        if (selected) Highlighted.Add(value.Trim());
    }
    public void WriteLine()
    {
        Output.AppendLine();
        CursorLeft = 0;
        CursorTop = UsesVirtualCursor ? CursorTop + 1 : Math.Min(CursorTop + 1, BufferHeight - 1);
    }
}
