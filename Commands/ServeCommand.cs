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
        var manifestPath = GetOpt(args, "--manifest", "-m");
        var noRenderer = args.Contains("--no-renderer");
        var rendererId = noRenderer ? "none" : GetOpt(args, "--renderer", "-r");
        if (!string.IsNullOrWhiteSpace(manifestPath) && !Path.IsPathRooted(manifestPath))
            manifestPath = Path.GetFullPath(manifestPath);
        int.TryParse(intervalStr, out var interval);

        // Positional path arg
        var path = ArgParser.GetPositional(args, 0) ?? ".";
        path = Path.GetFullPath(path);

        // If --template given, expand it first
        if (!string.IsNullOrEmpty(templatePath))
        {
            ConOut.WriteLine("[V12.Sdk] Expanding template from '{templatePath}'...");
            var tExit = PwshUtil.RunProcess(compilerExe, $"new \"{Path.GetFileName(path)}\" --template \"{templatePath}\"", Environment.CurrentDirectory);
            if (tExit != 0) return 1;
            path = Path.Combine(Environment.CurrentDirectory, Path.GetFileName(path));
        }

        if (!Directory.Exists(path))
        {
            ConOut.WriteError($"Error: directory '{path}' not found.");
            return 1;
        }

        // A .NET gameplay project has no contract.ctproj: watch its sources and
        // rerun the precompiled pack flow instead.
        var ctProjPath = FindCtproj(path);
        if (ctProjPath == null)
        {
            if (PackCommand.FindProject(path) != null) return ServeDotnet(path, args);
            ConOut.WriteError($"Error: no contract.ctproj or .csproj found in '{path}'.");
            return 1;
        }

        // Find contracts and XMLs to watch
        var ctFiles = DiscoverContractFiles(path);
        var xmlFiles = Directory.GetFiles(path, "*.xml", SearchOption.AllDirectories).ToList();
        var watchPaths = ctFiles.Concat(xmlFiles).Distinct().ToList();

        if (watchPaths.Count == 0)
        {
            ConOut.WriteError($"Error: no .ct or .xml files found in '{path}'.");
            return 1;
        }

        var proj = CtProject.Read(File.ReadAllText(ctProjPath));
        var projectName = string.IsNullOrWhiteSpace(proj.Name) ? Path.GetFileName(path) : proj.Name;
        var outPak = $"{projectName}.v12pak";

        ConOut.WriteLine("[V12.Sdk] Watch mode — polling every {0}ms", interval);
        ConOut.WriteLine("[V12.Sdk] Watching {0} file(s)...", watchPaths.Count);
        ConOut.WriteLine("[V12.Sdk] Press Ctrl+C to stop\n");

        var lastBuild = DateTime.UtcNow;
        var buildCount = 0;

                    RunBuild(watchPaths, path, output, copyTarget, manifestPath, outPak, compilerExe, rendererId, ref buildCount, ref lastBuild);

        // Simple cancel handling
        var cancelled = false;
        Console.CancelKeyPress += (_, e) => { cancelled = true; e.Cancel = true; };

        try
        {
            while (!cancelled)
            {
                Thread.Sleep(interval);
                if (HasChanges(watchPaths, lastBuild))
        RunBuild(watchPaths, path, output, copyTarget, manifestPath, outPak, compilerExe, rendererId, ref buildCount, ref lastBuild);
            }
        }
        catch (OperationCanceledException) { }

        ConOut.WriteLine("\n[V12.Sdk] Stopped.");
        return 0;
    }

    /// <summary>Watch-mode loop for .NET gameplay projects: republish + repack on changes.</summary>
    static int ServeDotnet(string path, string[] args)
    {
        var intervalStr = GetOpt(args, "--interval", "-i") ?? "500";
        int.TryParse(intervalStr, out var interval);
        if (interval <= 0) interval = 500;
        var copyTarget = GetOpt(args, "--copy-target", "-c");

        var forwarded = new List<string>();
        ForwardOpt(args, forwarded, "--output", "-o");
        ForwardOpt(args, forwarded, "--manifest", "-m");
        ForwardOpt(args, forwarded, "--renderer", "-r");
        ForwardOpt(args, forwarded, "--configuration");
        ForwardOpt(args, forwarded, "--runtime");
        if (args.Contains("--no-renderer")) forwarded.Add("--no-renderer");
        if (args.Contains("--live-scripts")) forwarded.Add("--live-scripts");
        if (args.Contains("--no-restore")) forwarded.Add("--no-restore");
        if (args.Contains("--strict")) forwarded.Add("--strict");
        if (!string.IsNullOrWhiteSpace(copyTarget))
        {
            forwarded.Add("--copy-target");
            forwarded.Add(copyTarget);
        }

        ConOut.WriteLine("[V12.Sdk] .NET watch mode — polling every {0}ms", interval);
        ConOut.WriteLine("[V12.Sdk] Watching .cs/.csproj/content under '{0}'...", path);
        ConOut.WriteLine("[V12.Sdk] Press Ctrl+C to stop\n");

        var cancelled = false;
        Console.CancelKeyPress += (_, e) => { cancelled = true; e.Cancel = true; };

        PackCommand.Execute(forwarded.ToArray(), path);
        var lastBuild = DateTime.UtcNow;

        try
        {
            while (!cancelled)
            {
                Thread.Sleep(interval);
                if (!HasChanges(EnumerateDotnetWatchFiles(path), lastBuild)) continue;

                ConOut.WriteLine("[{0}] Rebuild...", DateTimeOffset.Now.ToString("HH:mm:ss"));
                PackCommand.Execute(forwarded.ToArray(), path);
                lastBuild = DateTime.UtcNow;
            }
        }
        catch (OperationCanceledException) { }

        ConOut.WriteLine("\n[V12.Sdk] Stopped.");
        return 0;
    }

    static List<string> EnumerateDotnetWatchFiles(string root)
    {
        var results = new List<string>();
        foreach (var pattern in new[] { "*.cs", "*.csproj", "*.xml", "*.worldml", "*.json" })
        {
            foreach (var file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file);
                var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (parts.Any(p => p.Equals("bin", StringComparison.OrdinalIgnoreCase)
                                   || p.Equals("obj", StringComparison.OrdinalIgnoreCase)
                                   || p.Equals(".git", StringComparison.OrdinalIgnoreCase)
                                   || p.Equals(".v12", StringComparison.OrdinalIgnoreCase)))
                    continue;
                results.Add(file);
            }
        }
        return results.Distinct().ToList();
    }

    static void ForwardOpt(string[] args, List<string> destination, params string[] names)
    {
        var value = GetOpt(args, names);
        if (string.IsNullOrWhiteSpace(value)) return;
        destination.Add(names[0]);
        destination.Add(value);
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

    static void RunBuild(List<string> watchPaths, string path, string? output, string? copyTarget, string? manifestPath,
                          string outPak, string compilerExe, string? rendererId, ref int buildCount, ref DateTime lastBuild)
    {
        buildCount++;
        ConOut.WriteLine("[{0}] Build #{1}...", DateTimeOffset.Now.ToString("HH:mm:ss"), buildCount);

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
            if (!string.IsNullOrWhiteSpace(manifestPath))
                V12.Sdk.V12PakPackager.PackGame(pakDir, outPath, manifestPath);
            else
                V12.Sdk.V12PakPackager.PackGame(pakDir, outPath);

            lastBuild = DateTime.UtcNow;
            ConOut.WriteLine("[{0}] OK ({1})", DateTimeOffset.Now.ToString("HH:mm:ss"), FormatBytes(new FileInfo(outPath).Length));
            RendererDeployment.CopyToOutput(outPath, rendererId);

            // Copy to target
            if (!string.IsNullOrEmpty(copyTarget))
            {
                File.Copy(outPath, Path.Join(copyTarget, outPak), true);
                RendererDeployment.CopyToOutput(Path.Join(copyTarget, outPak), rendererId);
                ConOut.WriteLine("[{0}] Copied to '{1}/'", DateTimeOffset.Now.ToString("HH:mm:ss"), copyTarget);
            }
        }
        catch (Exception ex)
        {
            ConOut.WriteLine("[{0}] FAILED — {1}", DateTimeOffset.Now.ToString("HH:mm:ss"), ex.Message);
        }
    }

    static string? GetOpt(string[] args, params string[] names)
    {
        for (int i = 0; i < args.Length; i++)
            if (names.Contains(args[i]) && i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                return args[++i];
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
