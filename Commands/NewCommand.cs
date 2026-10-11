namespace V12.Sdk.Commands;

static class NewCommand
{
    public static int Handle(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrEmpty(args[0]))
        {
            Console.Error.WriteLine("Error: missing project name.\nUsage: vcsdk new <name>");
            return 1;
        }

        var name = args[0];
        var rootPath = Directory.GetCurrentDirectory();
        var dir = Path.Combine(rootPath, name);

        if (Directory.Exists(dir))
        {
            Console.Error.WriteLine($"Error: directory '{dir}' already exists.");
            return 1;
        }

        Directory.CreateDirectory(dir);

        // contract.ctproj
        var ctProj = "{\n" +
                     $"  \"Name\": \"{name}\",\n" +
                     "  \"Type\": \"exe\",\n" +
                     "  \"Main\": \"scripts/Main.ct\",\n" +
                     "  \"Output\": \"bin\"\n" +
                     "}\n";
        File.WriteAllText(Path.Join(dir, "contract.ctproj"), ctProj);

        // Main.ct
        var titleCase = char.ToUpper(name[0]) + name.Substring(1).ToLower();
        var mainCt = "// " + titleCase + " — Contract entry script\n" +
            "import V12.Core;\n" +
            "import V12.Basic;\n" +
            "\n" +
            "Contract " + titleCase + " {\n" +
            "    static fn Main() {\n" +
            "        IO.Println(\"\"[" + titleCase + "] game starting\"\");\n" +
            "\n" +
            "        // Spawn an example box into the world\n" +
            "        var cube = new Element(\"\"cube\"\");\n" +
            "        cube.AddComponent(new BoxMesh());\n" +
            "        GameRoot.SelectedWorld.AddElement(cube);\n" +
            "\n" +
            "        Log.Info(\"\"[" + titleCase + "] demo cube spawned\"\");\n" +
            "    }\n" +
            "\n" +
            "    static fn OnUpdate(dt: float) {\n" +
            "        // TODO: game logic here\n" +
            "    }\n" +
            "}\n";
        Directory.CreateDirectory(Path.Join(dir, "scripts"));
        File.WriteAllText(Path.Join(dir, "scripts", "Main.ct"), mainCt);

        // Default world
        var worldDir = Path.Join(dir, "worlds", "DefaultWorld");
        Directory.CreateDirectory(worldDir);
        var worldXml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<World name=\"DefaultWorld\">\n" +
            "    <SpawnPosition x=\"0.0\" y=\"1.5\" z=\"0.0\" />\n" +
            "</World>\n";
        File.WriteAllText(Path.Join(worldDir, "world.xml"), worldXml);

        // .gitignore
        var gitignore = "# Compiled outputs\n" +
            "bin/\nobj/\n\n" +
            "# Package cache\n.purr/\n\n" +
            "# IDE\n.vs/\n.vscode/\n\n" +
            "# OS\n.DS_Store\nThumbs.db\n";
        File.WriteAllText(Path.Join(dir, ".gitignore"), gitignore);

        Console.WriteLine($"Created Contract game project '{name}' at '{dir}'");
        Console.WriteLine($"  scripts/Main.ct        — entry script (Main + OnUpdate)");
        Console.WriteLine($"  worlds/DefaultWorld/   — default world definition");
        Console.WriteLine($"  contract.ctproj        — Contract project config");
        return 0;
    }
}
