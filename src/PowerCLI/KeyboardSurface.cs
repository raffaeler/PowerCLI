using System.Runtime.InteropServices;

namespace PowerCLI;

/// <summary>Raw keyboard and cursor primitives, independent of the process console.</summary>
public interface IConsoleInteractionSurface
{
    int CursorLeft { get; }
    int CursorTop { get; }
    int WindowWidth { get; }
    int BufferHeight { get; }
    bool UsesVirtualCursor { get; }
    bool EnsureBufferHeight(int minimumHeight);
    ValueTask<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken = default);
    void SetCursorPosition(int left, int top);
    void Write(string value, bool selected = false);
    void WriteLine();
}

/// <summary>Uses real key events and relative cursor movement for modern Windows terminals.</summary>
public sealed class SystemConsoleInteractionSurface : IConsoleInteractionSurface
{
    private readonly bool _usesVirtualCursor = TryEnableVirtualTerminalProcessing();
    private int _cursorLeft;
    private int _cursorTop;

    public int CursorLeft => _usesVirtualCursor ? _cursorLeft : Console.CursorLeft;
    public int CursorTop => _usesVirtualCursor ? _cursorTop : Console.CursorTop;
    public int WindowWidth => Math.Max(1, Console.WindowWidth);
    public int BufferHeight => Math.Max(1, Console.BufferHeight);
    public bool UsesVirtualCursor => _usesVirtualCursor;

    public bool EnsureBufferHeight(int minimumHeight)
    {
        if (!OperatingSystem.IsWindows() || minimumHeight <= BufferHeight)
        {
            return minimumHeight <= BufferHeight;
        }

        try
        {
            Console.BufferHeight = Math.Max(minimumHeight, Console.WindowHeight);
            return BufferHeight >= minimumHeight;
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or IOException or PlatformNotSupportedException)
        {
            // Fixed-buffer terminals are handled by scrolling the editor's reserved rows.
            return false;
        }
    }

    public async ValueTask<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken = default)
    {
        while (!Console.KeyAvailable)
        {
            await Task.Delay(15, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Console.ReadKey(intercept: true);
    }

    public void Write(string value, bool selected = false)
    {
        if (selected)
        {
            var foreground = Console.ForegroundColor;
            var background = Console.BackgroundColor;
            try
            {
                Console.BackgroundColor = ConsoleColor.DarkCyan;
                Console.ForegroundColor = ConsoleColor.Black;
                Console.Write(value);
            }
            finally
            {
                Console.ForegroundColor = foreground;
                Console.BackgroundColor = background;
            }
        }
        else
        {
            Console.Write(value);
        }

        if (_usesVirtualCursor)
        {
            foreach (var character in value)
            {
                if (character == '\n')
                {
                    _cursorLeft = 0;
                    _cursorTop++;
                }
                else if (character == '\r')
                {
                    _cursorLeft = 0;
                }
                else if (++_cursorLeft >= WindowWidth)
                {
                    _cursorLeft = 0;
                    _cursorTop++;
                }
            }
        }
    }

    public void WriteLine() => Write(Environment.NewLine);

    public void SetCursorPosition(int left, int top)
    {
        if (!_usesVirtualCursor)
        {
            Console.SetCursorPosition(left, top);
            return;
        }

        var offset = top - _cursorTop;
        if (offset != 0)
        {
            Console.Write($"\u001b[{Math.Abs(offset)}{(offset < 0 ? 'A' : 'B')}");
        }

        Console.Write('\r');
        if (left > 0)
        {
            Console.Write($"\u001b[{left}C");
        }

        _cursorLeft = left;
        _cursorTop = top;
    }

    private static bool TryEnableVirtualTerminalProcessing()
    {
        if (!OperatingSystem.IsWindows() || Console.IsOutputRedirected)
        {
            return false;
        }

        var handle = GetStdHandle(-11);
        return handle != IntPtr.Zero && handle != new IntPtr(-1) &&
            GetConsoleMode(handle, out var mode) && SetConsoleMode(handle, mode | 0x0004);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConsoleMode(IntPtr handle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleMode(IntPtr handle, uint mode);
}
