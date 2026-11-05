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

        ConOut.WriteLine("V12 SDK — doctor\n");

        // Purr home
        ConOut.WriteLine($"Purr home: {Purr.Home}");
        ConOut.WriteLine($"  exists: {Directory.Exists(Purr.Home)}");
        ConOut.WriteLine($"  packages: {Purr.PackagesDir} {(Directory.Exists(Purr.PackagesDir) ? "" : "(missing)")}");
        ConOut.WriteLine($"  bin: {Purr.BinDir} {(Directory.Exists(Purr.BinDir) ? "" : "(missing)")}");
        ConOut.WriteLine($"  renderers: {Purr.RenderersDir} {(Directory.Exists(Purr.RenderersDir) ? "" : "(missing)")}");
        ConOut.WriteLine($"  nova: {Purr.NovaRendererDir} {(Purr.HasNovaRenderer ? "(found)" : "(not found)")}");

        // Packages
        var hasFeed = Purr.HasPackagesFeed;
        ConOut.WriteLine($"\nPackages feed: {(hasFeed ? "ready" : "empty/missing")}");
        if (Directory.Exists(Purr.PackagesDir))
        {
            var nupkgs = Directory.GetFiles(Purr.PackagesDir, "*.nupkg", SearchOption.TopDirectoryOnly);
            ConOut.WriteLine($"  {nupkgs.Length} nupkg(s) in {Purr.PackagesDir}");
            foreach (var f in nupkgs.OrderByDescending(f => new FileInfo(f).LastWriteTime).Take(10))
            {
                var fi = new FileInfo(f);
                ConOut.WriteLine($"    {fi.Name} ({fi.Length / 1024} KB) {fi.LastWriteTime:yyyy-MM-dd HH:mm}");
            }
            if (nupkgs.Length == 0) ConOut.WriteLine($"    (run vcsdk sync to populate)");
        }

        // Tools
        ConOut.WriteLine($"\nTools:");
        foreach (var name in new[] { "ccl", "contract", "dotnet", "git" })
        {
            var found = Purr.FindTool(name) ?? FindOnPath(name);
            ConOut.WriteLine($"  {name}: {(found ?? "(not found)")} {(found != null ? "✓" : "")}");
        }
        // ccl via Purr
        var ccl = Purr.GetCclExe("ccl");
        ConOut.WriteLine($"\nEffective ccl: {ccl}");

        // MSBuild args
        ConOut.WriteLine($"\nMSBuild args (from Purr.GetMsBuildArgs):");
        var msb = Purr.GetMsBuildArgs(Directory.GetCurrentDirectory());
        ConOut.WriteLine($"  {msb}");

        // Monorepo cache
        var src = Path.Combine(Purr.Home, "src", "V12-Monorepo");
        ConOut.WriteLine($"\nMonorepo cache: {src} {(Directory.Exists(src) ? "(cached)" : "(not cached)")}");

        var localDev = @"D:\git\V12-Monorepo";
        if (Directory.Exists(localDev))
            ConOut.WriteLine($"Local dev monorepo: {localDev} (exists)");

        // NuGet sources
        ConOut.WriteLine($"\nNuGet sources:");
        var feeds = Purr.GetNuGetFeedPaths();
        foreach (var f in feeds) ConOut.WriteLine($"  {f}");
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
            ConOut.WriteLine(outp);
        }
        catch { }

        // Suggestions
        ConOut.WriteLine("\nSuggestions:");
        if (!hasFeed) ConOut.WriteLine("  - Run vcsdk sync to populate ~/.purr/packages");
        if (!Purr.HasNovaRenderer) ConOut.WriteLine("  - Nova renderer not found at ~/.purr/renderers/nova (optional, for Godot hosts)");
        if (Purr.FindTool("ccl") == null && FindOnPath("ccl") == null) ConOut.WriteLine("  - ccl not found on PATH nor ~/.purr/bin (install Contract CLI)");

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
        ConOut.WriteLine("""
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
