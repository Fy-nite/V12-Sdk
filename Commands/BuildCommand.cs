using System.Text.Json;

namespace V12.Sdk.Commands;

static class BuildCommand
{
    public static int Handle(string[] args)
    {
        // Parse options: --output/-o <val>, --compiler/-x <val>, --template/-t <val>, --help/-h
        var output = GetOpt(args, "--output", "-o");
        var compilerExe = GetOpt(args, "--compiler", "-x") ?? "ccl";
        var templatePath = GetOpt(args, "--template", "-t");  // NEW

        // Positional arg: path (first non-option token)
        var path = GetPositional(args, 0) ?? ".";
        path = Path.GetFullPath(path);

        // If --template given, expand it first (creates a new project dir in cwd)
        if (!string.IsNullOrEmpty(templatePath))
        {
            Console.WriteLine($"[V12.Sdk] Expanding template from '{templatePath}'...");
            var resolvedTemplate = ResolveTemplate(templatePath);
            if (!Directory.Exists(resolvedTemplate))
            {
                Console.Error.WriteLine($"Error: template not found: '{templatePath}'");
                return 1;
            }
            var tExit = PwshUtil.RunProcess(compilerExe, $"new \"{Path.GetFileName(path)}\" --template \"{resolvedTemplate}\"", Environment.CurrentDirectory);
            if (tExit != 0) return 1;
            // After expansion, re-resolve path as the newly created dir
            path = Path.Combine(Environment.CurrentDirectory, Path.GetFileName(path));
        }

        if (!Directory.Exists(path))
        {
            Console.Error.WriteLine($"Error: directory '{path}' not found.");
            return 1;
        }

        // Find contract.ctproj
        var ctProjPath = FindCtproj(path);
        if (ctProjPath == null)
        {
            Console.WriteLine($"Error: no contract.ctproj found in '{path}' or parent directories.");
            return 1;
        }

        Console.WriteLine($"[V12.Sdk] Found project: {ctProjPath}");

        var proj = CtProject.Read(File.ReadAllText(ctProjPath));
        var projectName = string.IsNullOrWhiteSpace(proj.Name) ? Path.GetFileName(path) : proj.Name;

        // Compile contracts via ccl
        Console.WriteLine("[V12.Sdk] Compiling contracts...");
        var exitCode = PwshUtil.RunProcess(compilerExe, "build", Path.GetDirectoryName(ctProjPath)!);
        if (exitCode != 0)
        {
            Console.Error.WriteLine("Error: compilation failed.");
            return 1;
        }

        // Pack v12pak
        var outPak = $"{projectName}.v12pak";
        var outPath = !string.IsNullOrEmpty(output)
            ? Path.IsPathRooted(output) ? output : Path.Combine(path, output)
            : Path.Join(Path.GetDirectoryName(ctProjPath)!, "bin", outPak);

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

        Console.WriteLine($"[V12.Sdk] Packing v12pak → '{outPath}'");
        V12.Pak.V12PakBuilder.Build(Path.GetDirectoryName(ctProjPath)!, outPath);
        var size = new FileInfo(outPath).Length;
        Console.WriteLine($"[V12.Sdk] Built {outPak} ({FormatBytes(size)})");
        return 0;
    }

    // Return the value for a flag (--flag value) or null if not present
    static string? GetOpt(string[] args, params string[] names)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (names.Contains(args[i]) && i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                return args[++i];
        }
        return null;
    }

    // Return the Nth positional argument (skip all -option flags)
    static string? GetPositional(string[] args, int n)
    {
        var count = -1;
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("-"))
            {
                if (++count == n) return args[i];
            }
        }
        return null;
    }

    static List<string> DiscoverWorlds(string gameDir)
    {
        var results = new List<string>();
        var worldsDir = Path.Join(gameDir, "worlds");
        if (!Directory.Exists(worldsDir)) return results;
        foreach (var d in Directory.GetDirectories(worldsDir))
        {
            var name = Path.GetFileName(d);
            if (File.Exists(Path.Join(d, "world.xml")) || File.Exists(Path.Join(d, "main.xml")))
                results.Add(name);
        }
        return results;
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

    /// <summary>Resolves a template name or path to an actual directory.</summary>
    static string ResolveTemplate(string templatePath)
    {
        // Absolute path — use as-is
        if (Path.IsPathRooted(templatePath) && Directory.Exists(templatePath))
            return templatePath;

        // Relative path — check from cwd first
        var fromCwd = Path.Combine(Environment.CurrentDirectory, templatePath);
        if (Directory.Exists(fromCwd))
            return fromCwd;

        // Named template — try multiple locations
        // 1. Bundled templates (in the publish directory next to the DLL)
        var exeFile = typeof(BuildCommand).Assembly.Location;
        var exeDir = Path.GetDirectoryName(exeFile)!;
        var bundled = Path.Join(exeDir, "Templates", templatePath);
        if (Directory.Exists(bundled)) return bundled;

        // Also try as a direct subdirectory of Templates/
        bundled = Path.Join(exeDir, "Templates", templatePath);
        if (Directory.Exists(bundled)) return bundled;

        // 2. Local project templates (.purr/templates/<name>)
        var local = Path.Join(".purr", "templates", templatePath);
        if (Directory.Exists(local)) return local;

        // 3. Global cache (~/.purr/templates/<name>)
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var global = Path.Join(home, ".purr", "templates", templatePath);
        if (Directory.Exists(global)) return global;

        // Fall back to original path
        return templatePath;
    }
}

/// <summary>Minimal reader for contract.ctproj files.</summary>
sealed class CtProject
{
    public required string Name { get; init; }

    public static CtProject Read(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new CtProject
        {
            Name = TryGetString(root, "Name") ?? "app",
        };
    }

    static string? TryGetString(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var val)) return null;
        return val.GetString();
    }
}
