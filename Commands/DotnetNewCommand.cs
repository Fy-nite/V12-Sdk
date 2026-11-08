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

        var projectName = ArgParser.GetPositional(args, 0);
        if (string.IsNullOrEmpty(projectName))
        {
            ConOut.WriteError("Error: missing project name.\nUsage: vcsdk new-dotnet <name>");
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
            ConOut.WriteError($"Error: directory '{rootPath}' already exists.");
            return 1;
        }

        // Find template directory
        var templateRoot = FindTemplateDir("blank-game");
        if (!Directory.Exists(templateRoot))
        {
            ConOut.WriteError($"Error: template 'blank-game' not found.");
            return 1;
        }

        ConOut.WriteLine($"[V12.Sdk] Creating .NET game project '{projectName}' at '{rootPath}'...");

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

        // Add scripts/ and worlds/ subdirectories
        Directory.CreateDirectory(Path.Combine(rootPath, "Scripts"));
        Directory.CreateDirectory(Path.Combine(rootPath, "worlds"));

        // Move DefaultScript.cs into Scripts/
        var scriptSrc = Path.Combine(rootPath, "DefaultScript.cs");
        if (File.Exists(scriptSrc))
        {
            var scriptDest = Path.Combine(rootPath, "Scripts", $"{lowerName}Script.cs");
            File.Move(scriptSrc, scriptDest, true);
        }

        // Move the sample scene into the pak world layout the packer scans.
        var sceneSrc = Path.Combine(rootPath, "scene.worldml");
        if (File.Exists(sceneSrc))
        {
            var worldDir = Path.Combine(rootPath, "worlds", projectName);
            Directory.CreateDirectory(worldDir);
            File.Move(sceneSrc, Path.Combine(worldDir, "world.xml"), true);
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
        var readme = $"# {projectName}\n\nA V12 game pak built with the .NET SDK.\n\n## Quick Start\n\n" +
                     $"1. Reference the V12 engine (see `{projectName}.csproj`)\n" +
                     $"2. `dotnet restore`\n" +
                     $"3. `vcsdk pack`\n" +
                     $"4. Run the generated renderer next to `bin/{projectName}.v12pak`\n";
        File.WriteAllText(Path.Combine(rootPath, "README.md"), readme);

        ConOut.WriteLine($"Created .NET game project '{projectName}':");
        ConOut.WriteLine($"  {projectName}.csproj     — project file (class library)");
        ConOut.WriteLine($"  GamePack.cs            — IV12Gamepack entry point");
        ConOut.WriteLine($"  Scripts/{lowerName}Script.cs   — component template");
        ConOut.WriteLine($"  worlds/{projectName}/world.xml — default scene");
        ConOut.WriteLine("\nNext steps:");
        ConOut.WriteLine($"  cd \"{rootPath}\"");
        ConOut.WriteLine("  # Edit the csproj to reference V12");
        ConOut.WriteLine("  dotnet restore");
        ConOut.WriteLine("  vcsdk pack");
        return 0;
    }

    static void ShowHelp()
    {
        ConOut.WriteLine("""
vcsdk new-dotnet <name> [options]

Scaffold a new V12 game pak as a .NET class library (C# + csproj). The generic
V12 renderer loads the published assembly as an IV12Gamepack.

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
