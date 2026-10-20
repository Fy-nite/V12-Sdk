namespace V12.Sdk.Commands;

static class DotnetNewCommand
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".worldml", ".json", ".md"
    };

    public static int Handle(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            ShowHelp();
            return 0;
        }

        var projectName = GetPositional(args, 0);
        if (string.IsNullOrEmpty(projectName))
        {
            Console.Error.WriteLine("Error: missing project name.\nUsage: vcsdk new-dotnet <name>");
            return 1;
        }

        // Parse options
        var outputDir = GetOpt(args, "--output", "-o");
        var backend = GetOpt(args, "--backend", "-b") ?? "none"; // none | monogame | godot

        var rootPath = outputDir != null
            ? Path.GetFullPath(outputDir)
            : Path.Combine(Directory.GetCurrentDirectory(), projectName);

        if (Directory.Exists(rootPath))
        {
            Console.Error.WriteLine($"Error: directory '{rootPath}' already exists.");
            return 1;
        }

        // Find template directory
        var templateRoot = FindTemplateDir("blank-game");
        if (!Directory.Exists(templateRoot))
        {
            Console.Error.WriteLine($"Error: template 'blank-game' not found.");
            return 1;
        }

        Console.WriteLine($"[V12.Sdk] Creating .NET game project '{projectName}' at '{rootPath}'...");

        // Build substitution map
        var lowerName = projectName.ToLowerInvariant();
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{name}"] = projectName,
            ["{name-to-lower}"] = lowerName,
        };

        // Read all text files from template
        var templateFiles = LoadTemplateFiles(templateRoot);
        foreach (var entry in templateFiles)
        {
            var dest = Path.Combine(rootPath, entry.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            string content = entry.Value;
            foreach (var kv in replacements)
                content = content.Replace(kv.Key, kv.Value);

            File.WriteAllText(dest, content);
        }

        // Add scripts/ and scenes/ subdirectories
        Directory.CreateDirectory(Path.Combine(rootPath, "Scripts"));
        Directory.CreateDirectory(Path.Combine(rootPath, "Scenes"));

        // Move DefaultScript.cs into Scripts/
        var scriptSrc = Path.Combine(rootPath, "DefaultScript.cs");
        if (File.Exists(scriptSrc))
        {
            var scriptDest = Path.Combine(rootPath, "Scripts", $"{lowerName}Script.cs");
            File.Move(scriptSrc, scriptDest, true);
        }

        // Move scene.worldml into Scenes/
        var sceneSrc = Path.Combine(rootPath, "scene.worldml");
        if (File.Exists(sceneSrc))
        {
            var sceneDest = Path.Combine(rootPath, "Scenes", $"{lowerName}Scene.worldml");
            File.Move(sceneSrc, sceneDest, true);
        }

        // Rename project file from BlankGame.csproj to name.csproj
        var blankCsproj = Path.Combine(rootPath, "BlankGame.csproj");
        if (File.Exists(blankCsproj))
        {
            File.Move(blankCsproj, Path.Combine(rootPath, $"{projectName}.csproj"));
        }

        // Adjust namespace in the moved script
        var finalScript = File.ReadAllText(Path.Combine(rootPath, "Scripts", $"{lowerName}Script.cs"));
        finalScript = finalScript.Replace("{name-to-lower}", lowerName);
        File.WriteAllText(Path.Combine(rootPath, "Scripts", $"{lowerName}Script.cs"), finalScript);

        // Clean up original locations (template copies)
        try { File.Delete(Path.Combine(rootPath, "DefaultScript.cs")); } catch { }
        try { File.Delete(Path.Combine(rootPath, "scene.worldml")); } catch { }

        // Write README
        var readme = $"# {projectName}\n\nA V12 engine game built with .NET SDK.\n\n## Quick Start\n\n" +
                     $"1. Reference V12 libraries (see `{projectName}.csproj`)\n" +
                     $"2. `dotnet restore`\n" +
                     $"3. `dotnet run`\n";
        File.WriteAllText(Path.Combine(rootPath, "README.md"), readme);

        Console.WriteLine($"Created .NET game project '{projectName}':");
        Console.WriteLine($"  {projectName}.csproj     — project file");
        Console.WriteLine($"  Program.cs             — game entry point");
        Console.WriteLine($"  Scripts/{lowerName}Script.cs   — component template");
        Console.WriteLine($"  Scenes/{lowerName}Scene.worldml — default scene");
        Console.WriteLine("\nNext steps:");
        Console.WriteLine($"  cd \"{rootPath}\"");
        Console.WriteLine("  # Edit csproj to reference your V12 libraries");
        Console.WriteLine("  dotnet restore");
        Console.WriteLine("  dotnet run");
        return 0;
    }

    static void ShowHelp()
    {
        Console.WriteLine("""
vcsdk new-dotnet <name> [options]

Scaffold a new V12 game project using the .NET SDK (C# + csproj).

Arguments:
  <name>                  Project name (also becomes the output directory)

Options:
  -o, --output <dir>      Output directory (default: ./<name>/)
  -b, --backend <type>    Target backend: monoGame or godot (default: none)
  -h, --help              Show help

Examples:
  vcsdk new-dotnet MyGame
  vcsdk new-dotnet MyGame -o ../projects
  vcsdk new-dotnet MyGame --backend monoGame
""");
    }

    static string? GetOpt(string[] args, params string[] names)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (names.Contains(args[i]) && i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                return args[++i];
        }
        return null;
    }

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

    static Dictionary<string, string> LoadTemplateFiles(string templateDir)
    {
        var files = new Dictionary<string, string>();
        foreach (var path in Directory.GetFiles(templateDir, "*.*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(path);
            if (!TextExtensions.Contains(ext)) continue;
            var relPath = Path.GetRelativePath(templateDir, path);
            files[relPath] = File.ReadAllText(path);
        }
        return files;
    }

    /// <summary>Finds a bundled template directory next to the executing assembly.</summary>
    static string FindTemplateDir(string name)
    {
        var exeFile = typeof(DotnetNewCommand).Assembly.Location;
        var exeDir = Path.GetDirectoryName(exeFile)!;
        
        // Try bundled templates first
        var bundled = Path.Join(exeDir, "templates", name);
        if (Directory.Exists(bundled)) return bundled;

        // Fallback: try relative to source tree
        var libDir = Path.Join(exeDir, "..", "..", "..");
        var srcTemplates = Path.Join(libDir, "V12-sdk", "templates", name);
        if (Directory.Exists(srcTemplates)) return srcTemplates;

        return Path.Join(exeDir, "templates", name);
    }
}
