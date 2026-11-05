using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace V12.Sdk;

/// <summary>
/// ANSI-colored console output for the CLI. Colors are only emitted when the
/// stream is a terminal and the environment allows it (<c>NO_COLOR</c> and
/// <c>TERM=dumb</c> are honored). Redirected output stays plain text, so scripts
/// and pipes are unaffected. Use <c>--no-color</c> / <c>--color</c> to override.
/// </summary>
static class ConOut
{
    private const string Esc = "\u001b[";
    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    private static bool _forced;
    private static bool _disabled;

    private static bool EnvAllowsColor
        => string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"))
           && !string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase);

    private static bool OutEnabled
        => !_disabled && (_forced || (!Console.IsOutputRedirected && EnvAllowsColor));

    private static bool ErrEnabled
        => !_disabled && (_forced || (!Console.IsErrorRedirected && EnvAllowsColor));

    static ConOut()
    {
        TryEnableVirtualTerminal(StdOutputHandle);
        TryEnableVirtualTerminal(StdErrorHandle);
    }

    /// <summary>Force colors on/off regardless of redirection (used by --color/--no-color).</summary>
    public static void SetColor(bool enabled)
    {
        _forced = enabled;
        _disabled = !enabled;
    }

    public static void WriteLine(string? text = null)
    {
        if (text == null) { Console.WriteLine(); return; }
        Console.WriteLine(ColorizeBlock(text, OutEnabled));
    }

    public static void WriteLine(string format, params object?[] args)
        => WriteLine(args.Length == 0 ? format : string.Format(format, args));

    public static void WriteError(string? text = null)
    {
        if (text == null) { Console.Error.WriteLine(); return; }
        Console.Error.WriteLine(ColorizeBlock(text, ErrEnabled));
    }

    public static void WriteError(string format, params object?[] args)
        => WriteError(args.Length == 0 ? format : string.Format(format, args));

    // ── Palette ─────────────────────────────────────────────────────────

    private static string Wrap(string text, string code)
        => text.Length == 0 ? text : $"{Esc}{code}m{text}{Esc}0m";

    private static string BoldCyan(string text) => Wrap(text, "1;36");
    private static string Cyan(string text) => Wrap(text, "36");
    private static string Green(string text) => Wrap(text, "32");
    private static string Yellow(string text) => Wrap(text, "33");
    private static string Red(string text) => Wrap(text, "31");
    private static string Magenta(string text) => Wrap(text, "35");

    // ── Colorizing ──────────────────────────────────────────────────────

    private static string ColorizeBlock(string text, bool enabled)
    {
        if (!enabled || text.Length == 0) return text;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
            lines[i] = ColorizeLine(lines[i]);
        return string.Join(Environment.NewLine, lines);
    }

    private static string ColorizeLine(string line)
    {
        if (line.Length == 0) return line;

        // [V12.Sdk], [sync], [HH:mm:ss] prefixes
        if (line[0] == '[')
        {
            int close = line.IndexOf(']');
            if (close > 0)
            {
                var tag = line.Substring(0, close + 1);
                var rest = line.Substring(close + 1);
                return Cyan(tag) + ColorizeRest(rest);
            }
        }

        if (line.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
            return Red(line);
        if (line.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase))
            return Red(line);
        if (line.StartsWith("PASS", StringComparison.Ordinal))
            return Green(line);

        // Headings such as "Commands:", "Options:", "Tools:"
        if (!char.IsWhiteSpace(line[0]) && line[^1] == ':' && line.Length > 1)
            return BoldCyan(line);

        // Usage / command headings
        if (line.StartsWith("vcsdk ", StringComparison.OrdinalIgnoreCase))
            return BoldCyan("vcsdk") + line.Substring(5);

        // Command headings and command-list rows ("build [path]", "  pack ...")
        var command = Regex.Match(line, @"^(\s*)(new-dotnet|new|build|pack|serve|install|sync|doctor|purr)\b");
        if (command.Success && (command.Groups[1].Value.Length == 0 || command.Groups[1].Value.Length >= 2))
        {
            var rest = line.Substring(command.Length);
            rest = Regex.Replace(rest, @"(<[^>]+>|\[[^\]]+\])", m => Magenta(m.Value));
            return command.Groups[1].Value + BoldCyan(command.Groups[2].Value) + rest;
        }

        // Help rows that start with a flag: color every --flag and <placeholder>.
        if (Regex.IsMatch(line, @"^\s+-{1,2}[A-Za-z]"))
        {
            line = Regex.Replace(line, @"(?<!\S)(--?[A-Za-z][\w-]*)", m => Cyan(m.Value));
            line = Regex.Replace(line, @"(<[^>]+>|\[[^\]]+\])", m => Magenta(m.Value));
            return line;
        }

        // Help rows that start with a placeholder: "  [path]   description"
        var placeholder = Regex.Match(line, @"^(\s+)(\[[^\]]+\]|<[^>]+>)(\s{2,})(\S.*)$");
        if (placeholder.Success)
            return placeholder.Groups[1].Value + Magenta(placeholder.Groups[2].Value)
                 + placeholder.Groups[3].Value + placeholder.Groups[4].Value;

        return ColorizeStatusWords(line);
    }

    private static string ColorizeRest(string rest)
    {
        if (rest.Contains("Error", StringComparison.OrdinalIgnoreCase)
            || rest.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || rest.Contains("FAILED", StringComparison.Ordinal))
            return Red(rest);

        if (rest.Contains("Warning", StringComparison.OrdinalIgnoreCase)
            || rest.Contains("skipping", StringComparison.OrdinalIgnoreCase)
            || rest.Contains("not found", StringComparison.OrdinalIgnoreCase))
            return Yellow(rest);

        if (rest.Contains("Built", StringComparison.OrdinalIgnoreCase)
            || rest.Contains("Created", StringComparison.OrdinalIgnoreCase)
            || rest.Contains("Found", StringComparison.OrdinalIgnoreCase)
            || rest.Contains("Installed", StringComparison.OrdinalIgnoreCase)
            || rest.Contains("loaded", StringComparison.OrdinalIgnoreCase)
            || rest.Contains("ready", StringComparison.OrdinalIgnoreCase)
            || rest.Contains("OK", StringComparison.Ordinal))
            return Green(rest);

        return rest;
    }

    private static string ColorizeStatusWords(string line)
    {
        return line
            .Replace("(not found)", Red("(not found)"))
            .Replace("(missing)", Red("(missing)"))
            .Replace("(not cached)", Yellow("(not cached)"))
            .Replace("(found)", Green("(found)"))
            .Replace("(cached)", Green("(cached)"))
            .Replace("(exists)", Green("(exists)"))
            .Replace("✓", Green("✓"));
    }

    // ── Windows VT enable ───────────────────────────────────────────────

    private static void TryEnableVirtualTerminal(int stdHandle)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            var handle = GetStdHandle(stdHandle);
            if (handle == 0 || handle == new nint(-1)) return;
            if (!GetConsoleMode(handle, out var mode)) return;
            if ((mode & EnableVirtualTerminalProcessing) != 0) return;
            SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing);
        }
        catch
        {
            // Fall back to plain output.
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(nint hConsoleHandle, uint dwMode);
}
