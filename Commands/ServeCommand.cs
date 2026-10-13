namespace V12.Sdk.Commands;

static class ServeCommand
{
    public static int Handle(string[] args)
    {
        var output = GetOpt(args, "--output", "-o");
        var compilerExe = GetOpt(args, "--compiler", "-x") ?? "ccl";
        var templatePath = GetOpt(args, "--template", "-t");  // NEW
        var intervalStr = GetOpt(args, "--interval", "-i") ?? "500";
        var copyTarget = GetOpt(args, "--copy-target", "-c");
        int.TryParse(intervalStr, out var interval);

        // Positional path arg
        var path = GetPositional(args, 0) ?? ".";
        path = Path.GetFullPath(path);

        // If --template given, expand it first
        if (!string.IsNullOrEmpty(templatePath))
        {
            Console.WriteLine("[V12.Sdk] Expanding template from '{templatePath}'...");
            var tExit = PwshUtil.RunProcess(compilerExe, $"new \"{Path.GetFileName(path)}\" --template \"{templatePath}\"", Environment.CurrentDirectory);
            if (tExit != 0) return 1;
            path = Path.Combine(Environment.CurrentDirectory, Path.GetFileName(path));
        }

        if (!Directory.Exists(path))
        {
            Console.Error.WriteLine($"Error: directory '{path}' not found.");
            return 1;
        }

        // Find contracts and XMLs to watch
        var ctFiles = DiscoverContractFiles(path);
        var xmlFiles = Directory.GetFiles(path, "*.xml", SearchOption.AllDirectories).ToList();
        var watchPaths = ctFiles.Concat(xmlFiles).Distinct().ToList();

        if (watchPaths.Count == 0)
        {
            Console.Error.WriteLine($"Error: no .ct or .xml files found in '{path}'.");
            return 1;
        }

        var ctProjPath = FindCtproj(path);
        var proj = CtProject.Read(File.ReadAllText(ctProjPath!));
        var projectName = string.IsNullOrWhiteSpace(proj.Name) ? Path.GetFileName(path) : proj.Name;
        var outPak = $"{projectName}.v12pak";

        Console.WriteLine("[V12.Sdk] Watch mode — polling every {0}ms", interval);
        Console.WriteLine("[V12.Sdk] Watching {0} file(s)...", watchPaths.Count);
        Console.WriteLine("[V12.Sdk] Press Ctrl+C to stop\n");

        var lastBuild = DateTime.UtcNow;
        var buildCount = 0;

        RunBuild(watchPaths, path, output, copyTarget, outPak, compilerExe, ref buildCount, ref lastBuild);

        // Simple cancel handling
        var cancelled = false;
        Console.CancelKeyPress += (_, e) => { cancelled = true; e.Cancel = true; };

        try
        {
            while (!cancelled)
            {
                Thread.Sleep(interval);
                if (HasChanges(watchPaths, lastBuild))
                    RunBuild(watchPaths, path, output, copyTarget, outPak, compilerExe, ref buildCount, ref lastBuild);
            }
        }
        catch (OperationCanceledException) { }

        Console.WriteLine("\n[V12.Sdk] Stopped.");
        return 0;
    }

    static List<string> DiscoverContractFiles(string dir)
    {
        var results = new List<string>();
        foreach (var d in new[] { "", "scripts", "src", "components" })
        {
            var subDir = d.Length > 0 ? Path.Join(dir, d) : dir;
            if (!Directory.Exists(subDir)) continue;
            results.AddRange(Directory.GetFiles(subDir, "*.ct", SearchOption.AllDirectories));
        }
        return results.Distinct().ToList();
    }

    static bool HasChanges(List<string> files, DateTime lastBuild)
    {
        foreach (var f in files)
            try { if (File.GetLastWriteTimeUtc(f) > lastBuild) return true; }
            catch { }
        return false;
    }

    static void RunBuild(List<string> watchPaths, string path, string? output, string? copyTarget, 
                          string outPak, string compilerExe, ref int buildCount, ref DateTime lastBuild)
    {
        buildCount++;
        Console.WriteLine("[{0}] Build #{1}...", DateTimeOffset.Now.ToString("HH:mm:ss"), buildCount);

        try
        {
            // Compile
            var exitCode = PwshUtil.RunProcess(compilerExe, "build", Path.GetDirectoryName(FindCtproj(path))!);
            if (exitCode != 0) throw new Exception("compilation failed");

            // Pack
            var pakDir = Path.GetDirectoryName(FindCtproj(path))!;
            var outPath = !string.IsNullOrEmpty(output)
                ? Path.IsPathRooted(output) ? output : Path.Combine(path, output)
                : Path.Join(pakDir, "bin", outPak);
            using (var writer = new V12.Pak.V12PakWriter(outPath))
            {
                var manifest = writer.PackFromDirectory(pakDir);
                writer.Finalize(manifest);
            }

            lastBuild = DateTime.UtcNow;
            Console.WriteLine("[{0}] OK ({1})", DateTimeOffset.Now.ToString("HH:mm:ss"), FormatBytes(new FileInfo(outPath).Length));

            // Copy to target
            if (!string.IsNullOrEmpty(copyTarget))
            {
                File.Copy(outPath, Path.Join(copyTarget, outPak), true);
                Console.WriteLine("[{0}] Copied to '{1}/'", DateTimeOffset.Now.ToString("HH:mm:ss"), copyTarget);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[{0}] FAILED — {1}", DateTimeOffset.Now.ToString("HH:mm:ss"), ex.Message);
        }
    }

    static string? GetOpt(string[] args, params string[] names)
    {
        for (int i = 0; i < args.Length; i++)
            if (names.Contains(args[i]) && i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                return args[++i];
        return null;
    }

    static string? GetPositional(string[] args, int n)
    {
        var count = -1;
        for (int i = 0; i < args.Length; i++)
            if (!args[i].StartsWith("-") && ++count == n) return args[i];
        return null;
    }

    static string? FindCtproj(string dir)
    {
        while (true)
        {
            var p = Path.Join(dir, "contract.ctproj");
            if (File.Exists(p)) return p;
            var parent = Directory.GetParent(dir);
            if (parent == null || !parent.Exists) break;
            dir = parent.FullName;
        }
        return null;
    }

    static string FormatBytes(long bytes)
    {
        string[] u = { "B", "KB", "MB", "GB" };
        double b = bytes; int i = 0;
        while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
        return $"{b:F1}{u[i]}";
    }
}
