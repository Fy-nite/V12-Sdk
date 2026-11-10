namespace V12.Sdk.Commands;

/// <summary>Scaffolds a .NET game pak that references the Purr package feed.</summary>
static class NetProjectScaffolder
{
    public static int Create(string name, string outputDir)
    {
        if (Directory.Exists(outputDir))
        {
            ConOut.WriteError($"Error: directory '{outputDir}' already exists.");
            return 1;
        }

        var template = TemplateScaffolder.Find("net");
        if (!Directory.Exists(template))
        {
            ConOut.WriteError($"Error: template 'net' not found at '{template}'.");
            return 1;
        }

        var identifier = TemplateScaffolder.ToIdentifier(name);
        var componentName = identifier.EndsWith("Script", StringComparison.OrdinalIgnoreCase)
            ? identifier
            : identifier + "Script";
        var version = Purr.ReadV12Version() ?? "1.0.0";
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{name}"] = name,
            ["{identifier}"] = identifier,
            ["{component-name}"] = componentName,
            ["{name-to-lower}"] = identifier.ToLowerInvariant(),
            ["{v12-version}"] = version,
        };

        TemplateScaffolder.Copy(template, outputDir, replacements);
        RenameIfExists(outputDir, "NetGame.csproj", name + ".csproj");
        Purr.EnsureNuGetConfig(outputDir);

        var scriptName = componentName;
        TemplateScaffolder.WriteReadme(Path.Combine(outputDir, "README.md"), $$"""
        # {{name}}

        A V12 game pak built with the .NET SDK. The assembly ships components and
        worlds; the renderer registers every concrete `IComponent` it finds, so no
        `IV12Gamepack` entry point is required.

        ## Quick Start

        1. `dotnet build`
        2. `vcsdk pack`
        3. Run `bin/{{name}}.exe`

        ## Layout

        - `Scripts/{{scriptName}}.cs` - the example component
        - `worlds/{{name}}/world.xml` - the scene that attaches it
        - `Directory.Build.props` - imports the Purr SDK properties

        ## Engine

        This project references `Finite.V12.Basic` from the Purr feed
        (`~/.purr/packages`). The version and feed are defined in
        `~/.purr/sdk/V12.props`, written by `vcsdk sync`.

        Run `vcsdk sync` once if the package feed is missing.
        """);

        if (!File.Exists(Purr.V12PropsPath) || !Purr.HasPackagesFeed)
        {
            ConOut.WriteError(
                "[V12.Sdk] Warning: V12 is not installed in Purr yet. " +
                "Run 'vcsdk sync' before building this project.");
        }

        ConOut.WriteLine($"Created .NET game pak '{name}' at '{outputDir}'");
        ConOut.WriteLine($"  {name}.csproj           - project file (class library)");
        ConOut.WriteLine("  Directory.Build.props   - imports ~/.purr/sdk/V12.props");
        ConOut.WriteLine($"  Scripts/{scriptName}.cs - component");
        ConOut.WriteLine($"  worlds/{name}/world.xml - default scene");
        ConOut.WriteLine();
        ConOut.WriteLine("Next steps:");
        ConOut.WriteLine($"  cd \"{outputDir}\"");
        ConOut.WriteLine("  dotnet build");
        ConOut.WriteLine("  vcsdk pack");
        return 0;
    }

    private static void RenameIfExists(string directory, string from, string to)
    {
        var source = Path.Combine(directory, from);
        if (File.Exists(source))
            File.Move(source, Path.Combine(directory, to));
    }
}
