namespace V12.Sdk.Commands;

/// <summary>
/// <c>vcsdk new &lt;kind&gt; --name &lt;name&gt;</c>. Kinds are explicit so each
/// scaffolder owns its own template, requirements and output layout.
/// </summary>
static class NewCommand
{
    public static int Handle(string[] args)
    {
        if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
        {
            ShowHelp();
            return 0;
        }

        var rawKind = ArgParser.GetPositional(args, 0);
        if (string.IsNullOrWhiteSpace(rawKind))
        {
            ConOut.WriteError("Error: missing project kind.\nUsage: vcsdk new <contract|net|renderer> --name <name>");
            return 1;
        }

        var kind = rawKind.ToLowerInvariant();
        if (kind is not ("contract" or "net" or "renderer"))
            return UnknownKind(rawKind);

        var name = ArgParser.GetOption(args, "--name", "-n");
        if (string.IsNullOrWhiteSpace(name))
        {
            ConOut.WriteError($"Error: --name is required.\nUsage: vcsdk new {kind} --name <name>");
            return 1;
        }

        var output = ArgParser.GetOption(args, "--output", "-o");
        var outputDir = string.IsNullOrWhiteSpace(output)
            ? Path.Combine(Directory.GetCurrentDirectory(), name)
            : Path.GetFullPath(output);

        return kind switch
        {
            "contract" => CreateContract(name, outputDir, ArgParser.GetOption(args, "--type") ?? "exe"),
            "net" => NetProjectScaffolder.Create(name, outputDir),
            "renderer" => RendererProjectScaffolder.Create(
                name,
                outputDir,
                ArgParser.GetOption(args, "--backend", "-b") ?? "none",
                ArgParser.GetOption(args, "--id"),
                ArgParser.GetOption(args, "--version", "-v")),
            _ => UnknownKind(kind),
        };
    }

    private static int UnknownKind(string kind)
    {
        ConOut.WriteError(
            $"Error: unknown project kind '{kind}'. Expected one of: contract, net, renderer.\n" +
            "Usage: vcsdk new <contract|net|renderer> --name <name>");
        return 1;
    }

    private static int CreateContract(string name, string outputDir, string type)
    {
        type = type.ToLowerInvariant();
        if (type is not ("exe" or "lib"))
        {
            ConOut.WriteError($"Error: --type must be 'exe' or 'lib' (got '{type}').");
            return 1;
        }

        if (Directory.Exists(outputDir))
        {
            ConOut.WriteError($"Error: directory '{outputDir}' already exists.");
            return 1;
        }

        var identifier = TemplateScaffolder.ToIdentifier(name);
        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(Path.Combine(outputDir, "scripts"));
        var worldDir = Path.Combine(outputDir, "worlds", "DefaultWorld");
        Directory.CreateDirectory(worldDir);

        var ctProj = $$"""
        {
          "Name": "{{name}}",
          "Type": "{{type}}",
          "Main": "scripts/Main.ct",
          "Output": "bin"
        }
        """;
        File.WriteAllText(Path.Combine(outputDir, "contract.ctproj"), ctProj);

        var mainCt = $$"""
        // {{identifier}} - Contract entry script
        import V12.Core;
        import V12.Basic;

        Contract {{identifier}} {
            static fn Main() {
                IO.Println("[{{identifier}}] game starting");

                // Spawn an example box into the world
                var cube = new Element("cube");
                cube.AddComponent(new BoxMesh());
                GameRoot.SelectedWorld.AddElement(cube);

                Log.Info("[{{identifier}}] demo cube spawned");
            }

            static fn OnUpdate(dt: float) {
                // TODO: game logic here
            }
        }
        """;
        File.WriteAllText(Path.Combine(outputDir, "scripts", "Main.ct"), mainCt);

        var worldXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <World name="DefaultWorld">
            <SpawnPosition x="0.0" y="1.5" z="0.0" />
        </World>
        """;
        File.WriteAllText(Path.Combine(worldDir, "world.xml"), worldXml);

        File.WriteAllText(Path.Combine(outputDir, ".gitignore"), """
        # Compiled outputs
        bin/
        obj/

        # Package cache
        .purr/

        # IDE
        .vs/
        .vscode/

        # OS
        .DS_Store
        Thumbs.db
        """);

        ConOut.WriteLine($"Created Contract project '{name}' at '{outputDir}'");
        ConOut.WriteLine("  contract.ctproj        - project config");
        ConOut.WriteLine("  scripts/Main.ct        - entry script (Main + OnUpdate)");
        ConOut.WriteLine("  worlds/DefaultWorld/   - default world");
        ConOut.WriteLine();
        ConOut.WriteLine("Next steps:");
        ConOut.WriteLine($"  cd \"{outputDir}\"");
        ConOut.WriteLine("  vcsdk build .");
        return 0;
    }

    private static void ShowHelp()
    {
        ConOut.WriteLine("""
        vcsdk new <kind> --name <name> [options]

        Scaffold a new project. Every kind takes its project name through --name.

        Kinds:
          contract            Contract (.ct) game project
          net                 .NET game pak (C# components + WorldML)
          renderer            V12 renderer library (new V12.Rendering API)

        Options:
          --name, -n <name>   Project name (required)
          -o, --output <dir>  Output directory (default: ./<name>)
          --type <exe|lib>    Contract project type (contract only)
          -b, --backend <t>   Renderer backend: none, monogame, godot (default none)
          --id <id>           renderer.json id (renderer only; default lowercased name)
          -h, --help          Show help

        Examples:
          vcsdk new contract --name MyGame
          vcsdk new net --name MyGame
          vcsdk new renderer --name MyRenderer --backend monogame

        .NET projects reference the engine from the Purr install feed
        (~/.purr/packages). Run `vcsdk sync` once to populate it.
        """);
    }
}
