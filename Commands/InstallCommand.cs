namespace V12.Sdk.Commands;

/// <summary>Installs Contract packages — delegates to ccl.</summary>
static class InstallCommand
{
    public static int Handle(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrEmpty(args[0]))
        {
            Console.Error.WriteLine("Error: missing package argument.\nUsage: vcsdk install <package|path/to/file.coi>");
            return 1;
        }

        var package = args[0];
        var compilerExe = GetOpt(args, "--compiler", "-x") ?? "ccl";
        var cwd = Directory.GetCurrentDirectory();

        // If it's a local file, use ccl install with the path
        if (File.Exists(package))
        {
            Console.WriteLine("[V12.Sdk] Installing from local file: {0}", package);
        }
        else
        {
            Console.WriteLine("[V12.Sdk] Installing package '{0}' from registry...", package);
        }

        var exitCode = PwshUtil.RunProcess(compilerExe, $"install \"{package}\"", cwd);
        Environment.Exit(exitCode);
        return exitCode;
    }

    static string? GetOpt(string[] args, params string[] names)
    {
        for (int i = 0; i < args.Length; i++)
            if (names.Contains(args[i]) && i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                return args[++i];
        return null;
    }
}
