namespace V12.Sdk.Commands;

/// <summary>
/// Scaffolds a renderer library that targets the backend-free
/// <c>V12.Rendering.IRenderer</c> contract. The library is meant to be copied
/// into an existing host (MonoGame, NovaV2, ...) or paired with a small loader
/// project next to it.
/// </summary>
static class RendererProjectScaffolder
{
    private static readonly HashSet<string> Backends = new(StringComparer.OrdinalIgnoreCase)
    {
        "none", "monogame", "godot",
    };

    public static int Create(string name, string outputDir, string backend, string? id, string? version)
    {
        backend = backend.ToLowerInvariant();
        if (!Backends.Contains(backend))
        {
            ConOut.WriteError(
                $"Error: unknown renderer backend '{backend}'. Expected one of: none, monogame, godot.");
            return 1;
        }

        if (Directory.Exists(outputDir))
        {
            ConOut.WriteError($"Error: directory '{outputDir}' already exists.");
            return 1;
        }

        var template = TemplateScaffolder.Find(Path.Combine("renderer", backend));
        if (!Directory.Exists(template))
        {
            ConOut.WriteError($"Error: renderer template '{backend}' not found at '{template}'.");
            return 1;
        }

        var identifier = TemplateScaffolder.ToIdentifier(name);
        var className = identifier.EndsWith("Renderer", StringComparison.OrdinalIgnoreCase)
            ? identifier
            : identifier + "Renderer";
        var rendererId = string.IsNullOrWhiteSpace(id)
            ? identifier.ToLowerInvariant()
            : id.Trim().ToLowerInvariant();
        var engineVersion = string.IsNullOrWhiteSpace(version)
            ? Purr.ReadV12Version() ?? "1.0.0"
            : version.Trim();

        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{name}"] = name,
            ["{identifier}"] = identifier,
            ["{class-name}"] = className,
            ["{name-to-lower}"] = identifier.ToLowerInvariant(),
            ["{id}"] = rendererId,
            ["{v12-version}"] = engineVersion,
            ["{backend}"] = backend,
            ["{now}"] = DateTime.UtcNow.ToString("O"),
        };

        TemplateScaffolder.Copy(template, outputDir, replacements);
        Purr.EnsureNuGetConfig(outputDir);

        var rendererClass = className;
        var distribution = backend == "godot" ? "godot-export" : "dotnet-singlefile";
        var singleFile = backend == "godot" ? "false" : "true";

        TemplateScaffolder.WriteReadme(Path.Combine(outputDir, "README.md"), $$"""
        # {{name}}

        A V12 renderer library targeting the backend-free `V12.Rendering.IRenderer`
        contract (`Submit(RenderFrameLease)`). The backend is `{{backend}}`.

        ## Layout

        - `{{identifier}}.Renderer.csproj` - class library
        - `{{rendererClass}}.cs` - renderer skeleton
        - `renderer.json` - install metadata for `vcsdk pack -r {{rendererId}}`
        - `Directory.Build.props` - imports the Purr SDK properties

        ## Wiring it into a host

        Either copy this folder into an existing engine host (for example
        `V12.Monogame/` or `NovaV2/`) and add a `ProjectReference`, or create a
        host project next to it. The host must:

        1. create `GameRoot`
        2. register the renderer: `root.Registry.Register("IRenderer", new {{rendererClass}}());`
        3. register engine services: `BasicRegistry.RegisterAll(root);`
        4. load a sibling `.v12pak` or a world
        5. each frame run `root.V12Tick(dt)`, capture a frame and submit it

        Minimal submit loop:

        ```csharp
        var producer = new RenderFrameProducer(renderer, width, height);

        root.V12Tick(deltaTime);
        if (root.ConsumeRenderDirty())
        {
            var snapshot = root.CaptureFrame();
            if (producer.TryBuild(snapshot, environment: null, time, deltaTime, out var lease)
                && lease is not null)
            {
                renderer.Submit(lease);
            }
        }
        ```

        `RenderFrameProducer`, `GameRoot` and `RenderEnvironmentData` live in
        `Finite.V12` / `Finite.V12.Rendering`. The host should reference those
        packages as well.

        ## Install as a Purr renderer

        The generated `renderer.json` is set for `{{backend}}`
        (`distribution: {{distribution}}`, `singleFile: {{singleFile}}`).
        Build the host, publish/export it, then copy the distribution to
        `~/.purr/renderers/{{rendererId}}/` next to this `renderer.json`.
        `vcsdk pack -r {{rendererId}}` will then copy it beside a game pak.

        ## Contract rules

        - Read only `RenderFrameLease` data. Do not reference `V12.Components.*`.
        - Ids are the only cross-thread identity.
        - Do not walk the world tree from the renderer.
        - Validate resource handles and device epochs before touching a GPU resource.
        """);

        if (!File.Exists(Purr.V12PropsPath) || !Purr.HasPackagesFeed)
        {
            ConOut.WriteError(
                "[V12.Sdk] Warning: V12 is not installed in Purr yet. " +
                "Run 'vcsdk sync' before building this project.");
        }

        ConOut.WriteLine($"Created V12 renderer '{name}' at '{outputDir}'");
        ConOut.WriteLine($"  {identifier}.Renderer.csproj - renderer library ({backend})");
        ConOut.WriteLine($"  {rendererClass}.cs - IRenderer skeleton");
        ConOut.WriteLine($"  renderer.json - id '{rendererId}'");
        ConOut.WriteLine();
        ConOut.WriteLine("Next steps:");
        ConOut.WriteLine($"  cd \"{outputDir}\"");
        ConOut.WriteLine("  dotnet build");
        ConOut.WriteLine("  # copy into an existing host, or add a host project next to it");
        return 0;
    }
}
