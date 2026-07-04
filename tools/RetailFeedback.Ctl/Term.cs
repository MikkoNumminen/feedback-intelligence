using System.Runtime.InteropServices;

namespace RetailFeedback.Ctl;

/// <summary>ANSI colour + status glyphs, mirroring ragctl's board vocabulary.
/// Colour is off when stdout is redirected or NO_COLOR is set.</summary>
public static class Term
{
    public static readonly bool UseColor =
        !Console.IsOutputRedirected
        && Environment.GetEnvironmentVariable("NO_COLOR") is null
        && EnableVirtualTerminal();

    public enum State { Ok, Busy, Warn, Down }

    // state -> (glyph, ANSI colour code)
    private static (string Glyph, string Code) Glyph(State s) => s switch
    {
        State.Ok => ("●", "32"),   // green
        State.Busy => ("◐", "33"), // yellow
        State.Warn => ("▲", "33"), // yellow
        _ => ("○", "31"),          // red
    };

    public static string C(string text, string code) => UseColor ? $"[{code}m{text}[0m" : text;

    public static string Bold(string text) => C(text, "1");

    public static string Line(string label, State state, string detail)
    {
        var (glyph, code) = Glyph(state);
        // Colour the dot and the value; leave the label in the default colour.
        return $"  {C(glyph, code)} {label,-24} {C(detail, code)}";
    }

    /// <summary>Windows consoles need VT processing explicitly enabled for ANSI
    /// escapes to render; on other platforms this is a no-op that returns true.</summary>
    private static bool EnableVirtualTerminal()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return true;
        try
        {
            var handle = GetStdHandle(-11); // STD_OUTPUT_HANDLE
            if (!GetConsoleMode(handle, out var mode))
                return false;
            return SetConsoleMode(handle, mode | 0x0004); // ENABLE_VIRTUAL_TERMINAL_PROCESSING
        }
        catch
        {
            return false;
        }
    }

    [DllImport("kernel32.dll")] private static extern nint GetStdHandle(int nStdHandle);
    [DllImport("kernel32.dll")] private static extern bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);
    [DllImport("kernel32.dll")] private static extern bool SetConsoleMode(nint hConsoleHandle, uint dwMode);
}
