namespace V12.Sdk.Commands;

static class SyncCommand
{
    public static int Handle(string[] args)
    {
        if (args.Any(a => a is "-h" or "--help"))
        {
            ShowHelp();
            return 0;
        }

        var monorepoOpt = GetOpt(args, "--monorepo", "--url");
        var refOpt = GetOpt(args, "--ref", "--branch") ?? "main";
        var versionOpt = GetOpt(args, "--version", "-v");
        var outputOpt = GetOpt(args, "--output", "-o");
        var force = args.Contains("--force") || args.Contains("-f");
        var noBuild = args.Contains("--no-build");
        var verbose = args.Contains("--verbose");

        // Resolve version
        var version = versionOpt ?? TryReadV12VersionFromMonorepo() ?? "1.0.0";

        Purr.EnsureHome();
        var packagesDir = !string.IsNullOrWhiteSpace(outputOpt)
            ? Path.GetFullPath(outputOpt)
            : Purr.PackagesDir;

        Directory.CreateDirectory(packagesDir);

        // Resolve monorepo source: --monorepo > env > local dev > remote default
        var envUrl = Environment.GetEnvironmentVariable("V12_MONOREPO_URL");
        var defaultUrl = "https://github.com/Fy-nite/V12.git";
        var monorepoUrl = monorepoOpt ?? envUrl ?? defaultUrl;

        // For local dev, if D:\git\V12-Monorepo exists and url is default, prefer local path to avoid clone
        var localDev = @"D:\git\V12-Monorepo";
        if (monorepoUrl == defaultUrl && Directory.Exists(Path.Combine(localDev, ".git")) && File.Exists(Path.Combine(localDev, ".gitmodules")))
        {
            Console.WriteLine($"[sync] Using local monorepo at {localDev} (override with --monorepo <url>)");
            monorepoUrl = localDev;
        }

        string? monorepoDir = null;
        var isLocalPath = Directory.Exists(monorepoUrl) || File.Exists(monorepoUrl);

        if (isLocalPath)
        {
            // Local path — use directly, no clone
            monorepoDir = Path.GetFullPath(monorepoUrl);
            Console.WriteLine($"[sync] Using local monorepo: {monorepoDir}");
            if (!File.Exists(Path.Combine(monorepoDir, ".gitmodules")) && !Directory.Exists(Path.Combine(monorepoDir, "V12-Engine")))
                Console.WriteLine($"[sync] Warning: {monorepoDir} doesn't look like V12-Monorepo (missing .gitmodules/V12-Engine)");
        }
        else
        {
            // Remote URL — clone/pull to Purr src cache
            var srcDir = Path.Combine(Purr.Home, "src");
            Directory.CreateDirectory(srcDir);
            monorepoDir = Path.Combine(srcDir, "V12-Monorepo");
            // If PURR_HOME cache already has it, pull; else clone
            if (Directory.Exists(Path.Combine(monorepoDir, ".git")))
            {
                Console.WriteLine($"[sync] Updating cached monorepo at {monorepoDir} (ref: {refOpt})...");
                var pull = PwshUtil.RunProcess("git", $"fetch origin", monorepoDir);
                if (pull != 0 && !force) return pull;
                // Try to checkout ref, fall back to main/master
                PwshUtil.RunProcess("git", $"checkout {refOpt}", monorepoDir);
                PwshUtil.RunProcess("git", $"pull --ff-only", monorepoDir);
                PwshUtil.RunProcess("git", $"submodule update --init --recursive", monorepoDir);
            }
            else
            {
                Console.WriteLine($"[sync] Cloning {monorepoUrl} -> {monorepoDir}...");
                // Ensure parent doesn't have stale non-git dir
                if (Directory.Exists(monorepoDir)) Directory.Delete(monorepoDir, true);
                var cloneArgs = $"clone --recurse-submodules --depth 1 --branch {refOpt} \"{monorepoUrl}\" \"{monorepoDir}\"";
                var exit = PwshUtil.RunProcess("git", cloneArgs, srcDir);
                if (exit != 0)
                {
                    // Try without --branch (maybe ref is commit) or without recurse
                    Console.WriteLine($"[sync] Clone with --branch {refOpt} failed, trying without branch...");
                    exit = PwshUtil.RunProcess("git", $"clone --recurse-submodules \"{monorepoUrl}\" \"{monorepoDir}\"", srcDir);
                    if (exit != 0) return exit;
                    PwshUtil.RunProcess("git", $"checkout {refOpt}", monorepoDir);
                    PwshUtil.RunProcess("git", $"submodule update --init --recursive", monorepoDir);
                }
            }
        }

        Console.WriteLine($"[sync] Packing Finite.* v{version} -> {packagesDir}");

        // Also try to read version from monorepo's Directory.Build.props if not specified
        if (string.IsNullOrWhiteSpace(versionOpt))
        {
            var monorepoVersion = TryReadV12Version(monorepoDir!);
            if (!string.IsNullOrWhiteSpace(monorepoVersion)) version = monorepoVersion!;
        }

        var projects = new[]
        {
            Path.Combine(monorepoDir!, "V12-Engine", "V12.csproj"),
            Path.Combine(monorepoDir!, "V12.Pak", "V12.Pak.csproj"),
        };

        var verbosity = verbose ? "minimal" : "quiet";
        var packed = 0;
        foreach (var proj in projects)
        {
            if (!File.Exists(proj))
            {
                Console.WriteLine($"[sync] Skip missing {proj}");
                continue;
            }
            var name = Path.GetFileNameWithoutExtension(proj);
            Console.WriteLine($"[sync] Packing {name} ({Path.GetFileName(proj)})...");
            var args2 = $"pack \"{proj}\" -c Release -o \"{packagesDir}\" -p:Version={version} --nologo -v {verbosity}";
            if (noBuild) args2 += " --no-build";
            var exit = PwshUtil.RunProcess("dotnet", args2, Path.GetDirectoryName(proj)!);
            if (exit != 0)
            {
                Console.Error.WriteLine($"[sync] dotnet pack failed for {proj} (exit {exit})");
                if (!force) return exit;
            }
            else packed++;
        }

        // Legacy mirror to D:\V12Packages for compat with existing Scramble-Studio global source
        var legacy = @"D:\V12Packages";
        if (Directory.Exists(legacy) && packagesDir != legacy)
        {
            foreach (var f in Directory.GetFiles(packagesDir, "Finite.V12*.nupkg"))
            {
                try { File.Copy(f, Path.Combine(legacy, Path.GetFileName(f)), true); } catch { }
            }
            Console.WriteLine($"[sync] Mirrored to {legacy}");
        }

        Console.WriteLine($"\n[sync] Done. Packed {packed} package(s) v{version} -> {packagesDir}");
        Console.WriteLine($"[sync] Feed ready. Try: vcsdk build");
        if (Directory.GetFiles(packagesDir, "*.nupkg").Length == 0)
        {
            Console.Error.WriteLine($"[sync] Warning: no nupkgs in {packagesDir}");
            return 1;
        }
        foreach (var f in Directory.GetFiles(packagesDir, "*.nupkg").OrderByDescending(f => new FileInfo(f).LastWriteTime).Take(5))
            Console.WriteLine($"  {Path.GetFileName(f)}");

        return 0;
    }

    static string? TryReadV12VersionFromMonorepo()
    {
        // Try local dev monorepo first, then cached
        foreach (var dir in new[] { @"D:\git\V12-Monorepo", Path.Combine(Purr.Home, "src", "V12-Monorepo") })
        {
            var v = TryReadV12Version(dir);
            if (v != null) return v;
        }
        return null;
    }

    static string? TryReadV12Version(string monorepoDir)
    {
        try
        {
            var props = Path.Combine(monorepoDir, "Directory.Build.props");
            if (!File.Exists(props)) return null;
            var text = File.ReadAllText(props);
            var m = System.Text.RegularExpressions.Regex.Match(text, @"<V12Version>([^<]+)</V12Version>");
            if (m.Success) return m.Groups[1].Value.Trim();
        }
        catch { }
        return null;
    }

    static string? GetOpt(string[] args, params string[] names)
    {
        for (int i = 0; i < args.Length; i++)
            if (names.Contains(args[i]) && i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                return args[++i];
        return null;
    }

    static void ShowHelp()
    {
        Console.WriteLine("""
vcsdk sync [options]

Sync Purr packages from the V12-Monorepo into ~/.purr/packages.
Clones/pulls the monorepo and runs dotnet pack for Finite.V12, Finite.V12.Pak.

Options:
  --monorepo <url|path>  Monorepo git URL or local path (default: https://github.com/Fy-nite/V12.git, or D:\git\V12-Monorepo if present)
  --ref <branch>         Git ref to sync (default: main)
  --version <ver>        Package version to pack (default: V12Version from Directory.Build.props, else 1.0.0)
  -o, --output <path>    Output packages dir (default: ~/.purr/packages or $PURR_HOME/packages)
  --force                Continue on error, overwrite existing
  --no-build             Pass --no-build to dotnet pack
  --verbose              Verbose pack output
  -h, --help             Show help

Env:
  V12_MONOREPO_URL       Override default monorepo URL
  PURR_HOME              Override ~/.purr location

Examples:
  vcsdk sync
  vcsdk sync --monorepo D:\git\V12-Monorepo --version 1.0.1
  vcsdk sync --ref main --verbose
  PURR_HOME=D:\purr vcsdk sync
""");
    }
}
