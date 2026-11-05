namespace V12.Sdk.Commands;

static class DoctorCommand
{
    public static int Handle(string[] args)
    {
        if (args.Any(a => a is "-h" or "--help"))
        {
            ShowHelp();
            return 0;
        }

        Console.WriteLine("V12 SDK — doctor\n");

        // Purr home
        Console.WriteLine($"Purr home: {Purr.Home}");
        Console.WriteLine($"  exists: {Directory.Exists(Purr.Home)}");
        Console.WriteLine($"  packages: {Purr.PackagesDir} {(Directory.Exists(Purr.PackagesDir) ? "" : "(missing)")}");
        Console.WriteLine($"  bin: {Purr.BinDir} {(Directory.Exists(Purr.BinDir) ? "" : "(missing)")}");
        Console.WriteLine($"  renderers: {Purr.RenderersDir} {(Directory.Exists(Purr.RenderersDir) ? "" : "(missing)")}");
        Console.WriteLine($"  nova: {Purr.NovaRendererDir} {(Purr.HasNovaRenderer ? "(found)" : "(not found)")}");

        // Packages
        var hasFeed = Purr.HasPackagesFeed;
        Console.WriteLine($"\nPackages feed: {(hasFeed ? "ready" : "empty/missing")}");
        if (Directory.Exists(Purr.PackagesDir))
        {
            var nupkgs = Directory.GetFiles(Purr.PackagesDir, "*.nupkg", SearchOption.TopDirectoryOnly);
            Console.WriteLine($"  {nupkgs.Length} nupkg(s) in {Purr.PackagesDir}");
            foreach (var f in nupkgs.OrderByDescending(f => new FileInfo(f).LastWriteTime).Take(10))
            {
                var fi = new FileInfo(f);
                Console.WriteLine($"    {fi.Name} ({fi.Length / 1024} KB) {fi.LastWriteTime:yyyy-MM-dd HH:mm}");
            }
            if (nupkgs.Length == 0) Console.WriteLine($"    (run vcsdk sync to populate)");
        }

        // Tools
        Console.WriteLine($"\nTools:");
        foreach (var name in new[] { "ccl", "contract", "dotnet", "git" })
        {
            var found = Purr.FindTool(name) ?? FindOnPath(name);
            Console.WriteLine($"  {name}: {(found ?? "(not found)")} {(found != null ? "✓" : "")}");
        }
        // ccl via Purr
        var ccl = Purr.GetCclExe("ccl");
        Console.WriteLine($"\nEffective ccl: {ccl}");

        // MSBuild args
        Console.WriteLine($"\nMSBuild args (from Purr.GetMsBuildArgs):");
        var msb = Purr.GetMsBuildArgs(Directory.GetCurrentDirectory());
        Console.WriteLine($"  {msb}");

        // Monorepo cache
        var src = Path.Combine(Purr.Home, "src", "V12-Monorepo");
        Console.WriteLine($"\nMonorepo cache: {src} {(Directory.Exists(src) ? "(cached)" : "(not cached)")}");

        var localDev = @"D:\git\V12-Monorepo";
        if (Directory.Exists(localDev))
            Console.WriteLine($"Local dev monorepo: {localDev} (exists)");

        // NuGet sources
        Console.WriteLine($"\nNuGet sources:");
        var feeds = Purr.GetNuGetFeedPaths();
        foreach (var f in feeds) Console.WriteLine($"  {f}");
        // Global dotnet sources
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "nuget list source",
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        try
        {
            var p = System.Diagnostics.Process.Start(psi)!;
            var outp = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            Console.WriteLine(outp);
        }
        catch { }

        // Suggestions
        Console.WriteLine("\nSuggestions:");
        if (!hasFeed) Console.WriteLine("  - Run vcsdk sync to populate ~/.purr/packages");
        if (!Purr.HasNovaRenderer) Console.WriteLine("  - Nova renderer not found at ~/.purr/renderers/nova (optional, for Godot hosts)");
        if (Purr.FindTool("ccl") == null && FindOnPath("ccl") == null) Console.WriteLine("  - ccl not found on PATH nor ~/.purr/bin (install Contract CLI)");

        return 0;
    }

    static string? FindOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            try
            {
                var cand = Path.Combine(dir, name);
                if (File.Exists(cand)) return cand;
                if (File.Exists(cand + ".exe")) return cand + ".exe";
                if (File.Exists(cand + ".cmd")) return cand + ".cmd";
            }
            catch { }
        }
        return null;
    }

    static void ShowHelp()
    {
        Console.WriteLine("""
vcsdk doctor

Diagnose Purr home, packages feed, tools, and monorepo cache.

Checks:
  ~/.purr/packages, ~/.purr/bin/ccl, ~/.purr/renderers/nova,
  MSBuild args, NuGet sources, cached monorepo.

Usage:
  vcsdk doctor
""");
    }
}
