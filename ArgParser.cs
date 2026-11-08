namespace V12.Sdk;

/// <summary>
/// Shared command-line parsing helpers for vcsdk. Keeps positional argument
/// detection from mistaking an option's value (e.g. <c>-r Nova</c>) for a path.
/// </summary>
static class ArgParser
{
    // Options that consume the following token, across every vcsdk command.
    static readonly HashSet<string> ValueOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "--output", "-o",
        "--compiler", "-x",
        "--template", "-t",
        "--manifest", "-m",
        "--renderer", "-r",
        "--configuration",
        "--runtime",
        "--copy-target", "-c",
        "--interval", "-i",
        "--backend", "-b",
        "--monorepo", "--url",
        "--ref", "--branch",
        "--version", "-v",
    };

    /// <summary>
    /// Accept single-dash long options as a convenience: <c>-renderer</c> and
    /// <c>--renderer</c> are equivalent. Short options (<c>-r</c>) are unchanged.
    /// </summary>
    public static string[] NormalizeLongOptions(string[] args)
    {
        return args
            .Select(arg => arg.Length > 2 && arg[0] == '-' && arg[1] != '-' ? "-" + arg : arg)
            .ToArray();
    }

    /// <summary>Returns the <paramref name="n"/>th positional argument, skipping options and their values.</summary>
    public static string? GetPositional(string[] args, int n)
    {
        var count = -1;
        for (int i = 0; i < args.Length; i++)
        {
            if (ValueOptions.Contains(args[i]))
            {
                i++; // skip the option's value
                continue;
            }

            if (args[i].StartsWith("-")) continue;

            if (++count == n) return args[i];
        }
        return null;
    }
}
