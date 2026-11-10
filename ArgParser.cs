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
        "--name", "-n",
        "--id",
        "--type",
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
        "--schema", "-s",
        "--assemblies", "-a",
        "--templates",
        "--links",
        "--include",
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

    /// <summary>
    /// Returns the value for an option, accepting both <c>--name value</c> and
    /// <c>--name=value</c>.
    /// </summary>
    public static string? GetOption(string[] args, params string[] names)
    {
        for (int i = 0; i < args.Length; i++)
        {
            foreach (var name in names)
            {
                if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                    return args[i][(name.Length + 1)..];
            }

            if (names.Contains(args[i]) && i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                return args[++i];
        }
        return null;
    }

    /// <summary>
    /// Returns every value for a repeatable option, accepting comma-separated
    /// values and both <c>--name value</c> and <c>--name=value</c>.
    /// </summary>
    public static List<string> GetOptions(string[] args, params string[] names)
    {
        var values = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            string? value = null;
            foreach (var name in names)
            {
                if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                {
                    value = args[i][(name.Length + 1)..];
                    break;
                }
            }

            if (value == null && names.Contains(args[i]) && i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                value = args[++i];

            if (value == null) continue;
            values.AddRange(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        return values;
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
