using System.Text.Json;
using V12.Sdk.Links;

namespace V12.Sdk.Commands;

/// <summary>
/// Checks (and optionally fixes) WorldML reference selectors before a pak is
/// packed. Uses components.schema.xml for type-aware validation when present,
/// otherwise falls back to attributes whose names end in "Ref".
/// </summary>
static class LinksCommand
{
    public static int Handle(string[] args)
    {
        if (args.Contains("-h") || args.Contains("--help"))
        {
            ShowHelp();
            return 0;
        }

        var path = ArgParser.GetPositional(args, 0) ?? ".";
        path = Path.GetFullPath(path);

        var schemaPath = GetOpt(args, "--schema", "-s");
        var manifestPath = GetOpt(args, "--manifest", "-m");
        var templateDirs = GetOpts(args, "--templates");
        var fix = args.Contains("--fix");
        var dryRun = args.Contains("--dry-run");
        var strict = args.Contains("--strict");
        var json = args.Contains("--json");

        if (!Directory.Exists(path) && !File.Exists(path))
        {
            ConOut.WriteError($"Error: path '{path}' not found.");
            return 1;
        }

        var root = Directory.Exists(path) ? path : Path.GetDirectoryName(path)!;

        ComponentSchema? schema = null;
        var resolvedSchema = schemaPath == null
            ? FindSchema(root)
            : (Path.IsPathRooted(schemaPath) ? schemaPath : Path.GetFullPath(schemaPath));

        if (resolvedSchema != null)
        {
            if (!File.Exists(resolvedSchema))
            {
                ConOut.WriteError($"Error: schema '{resolvedSchema}' not found.");
                return 1;
            }

            try
            {
                schema = ComponentSchema.Load(resolvedSchema);
            }
            catch (Exception ex)
            {
                ConOut.WriteError($"Error: could not read schema '{resolvedSchema}': {ex.Message}");
                return 1;
            }
        }
        else
        {
            ConOut.WriteLine(
                "[V12.Sdk] No components.schema.xml found; checking only that *Ref targets exist. " +
                "Run 'vcsdk schema' for type-aware validation.");
        }

        var templates = new TemplateMap();
        foreach (var dir in templateDirs)
            TemplateMap.LoadDirectory(templates, Path.IsPathRooted(dir) ? dir : Path.GetFullPath(dir));
        foreach (var dir in DiscoverTemplateDirectories(root))
            TemplateMap.LoadDirectory(templates, dir);

        var checker = new WorldMLinkChecker(schema, fix, dryRun, templates);
        var result = Directory.Exists(path)
            ? checker.CheckDirectory(path)
            : checker.CheckFile(path);

        CheckManifest(resolvedManifest(root, manifestPath), root, result);

        if (json)
            PrintJson(path, resolvedSchema, result);
        else
            PrintText(result, dryRun);

        if (result.Errors > 0 && strict)
            return 1;

        return 0;
    }

    private static string? resolvedManifest(string root, string? manifestPath)
    {
        if (!string.IsNullOrWhiteSpace(manifestPath))
            return Path.IsPathRooted(manifestPath) ? manifestPath : Path.GetFullPath(manifestPath);

        var candidate = Path.Combine(root, "manifest.json");
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? FindSchema(string root)
    {
        var candidate = Path.Combine(root, "components.schema.xml");
        return File.Exists(candidate) ? candidate : null;
    }

    private static List<string> DiscoverTemplateDirectories(string root)
    {
        var result = new List<string>();

        var templates = Path.Combine(root, "templates");
        if (Directory.Exists(templates)) result.Add(templates);

        var worlds = Path.Combine(root, "worlds");
        if (Directory.Exists(worlds))
        {
            foreach (var world in Directory.EnumerateDirectories(worlds))
            {
                var worldTemplates = Path.Combine(world, "templates");
                if (Directory.Exists(worldTemplates)) result.Add(worldTemplates);
            }
        }

        var manifest = Path.Combine(root, "manifest.json");
        if (!File.Exists(manifest)) return result;

        try
        {
            using var stream = File.OpenRead(manifest);
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("worlds", out var worldsArray)
                || worldsArray.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var world in worldsArray.EnumerateArray())
            {
                if (!world.TryGetProperty("templatesDir", out var dirElement)) continue;
                var relative = dirElement.GetString();
                if (string.IsNullOrWhiteSpace(relative)) continue;
                var full = Path.Combine(root,
                    relative!.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(full)) result.Add(full);
            }
        }
        catch
        {
            // The manifest check reports malformed manifests separately.
        }

        return result;
    }

    private static List<string> GetOpts(string[] args, params string[] names)
        => ArgParser.GetOptions(args, names);

    private static void CheckManifest(string? manifestPath, string root, LinkCheckResult result)
    {
        if (manifestPath == null || !File.Exists(manifestPath)) return;

        try
        {
            using var stream = File.OpenRead(manifestPath);
            using var document = JsonDocument.Parse(stream);
            var manifestRoot = document.RootElement;

            void CheckPath(string? relative, string label)
            {
                if (string.IsNullOrWhiteSpace(relative)) return;
                var full = Path.Combine(root,
                    relative!.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(full) || Directory.Exists(full)) return;

                result.Diagnostics.Add(new LinkDiagnostic
                {
                    Severity = LinkSeverity.Error,
                    File = manifestPath,
                    Message = $"Manifest {label} path '{relative}' does not exist.",
                });
            }

            if (manifestRoot.TryGetProperty("worlds", out var worlds) && worlds.ValueKind == JsonValueKind.Array)
            {
                foreach (var world in worlds.EnumerateArray())
                {
                    if (world.TryGetProperty("entry", out var entry)) CheckPath(entry.GetString(), "world entry");
                    if (world.TryGetProperty("templatesDir", out var templates)) CheckPath(templates.GetString(), "templates directory");
                }
            }

            foreach (var property in new[] { "paks", "scripts", "cSharpScripts", "dependencies" })
            {
                if (!manifestRoot.TryGetProperty(property, out var list) || list.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var item in list.EnumerateArray())
                    CheckPath(item.GetString(), property);
            }

            if (manifestRoot.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Object)
            {
                foreach (var asset in assets.EnumerateObject())
                    CheckPath(asset.Value.GetString(), "asset");
            }
        }
        catch (Exception ex)
        {
            result.Diagnostics.Add(new LinkDiagnostic
            {
                Severity = LinkSeverity.Warning,
                File = manifestPath,
                Message = $"Could not read manifest: {ex.Message}",
            });
        }
    }

    private static void PrintText(LinkCheckResult result, bool dryRun)
    {
        foreach (var diagnostic in result.Diagnostics)
        {
            var location = diagnostic.Line > 0
                ? $"{diagnostic.File}:{diagnostic.Line}"
                : diagnostic.File;
            var subject = string.IsNullOrEmpty(diagnostic.ComponentType)
                ? diagnostic.ElementPath
                : $"{diagnostic.ElementPath} [{diagnostic.ComponentType}.{diagnostic.Property}]";
            var prefix = diagnostic.Severity switch
            {
                LinkSeverity.Error => "error",
                LinkSeverity.Warning => "warning",
                _ => "info",
            };
            ConOut.WriteLine($"{location}: {prefix}: {subject}: {diagnostic.Message}");
        }

        ConOut.WriteLine(
            $"[V12.Sdk] Checked {result.FilesChecked} scene(s), {result.RefsChecked} reference(s): " +
            $"{result.Errors} error(s), {result.Warnings} warning(s)" +
            (result.RefsFixed > 0 ? $", {result.RefsFixed} fixed" : "") +
            (dryRun ? " (dry run)" : "") + ".");
    }

    private static void PrintJson(string path, string? schema, LinkCheckResult result)
    {
        var payload = new
        {
            path,
            schema,
            files = result.FilesChecked,
            refs = result.RefsChecked,
            errors = result.Errors,
            warnings = result.Warnings,
            fixedRefs = result.RefsFixed,
            diagnostics = result.Diagnostics.Select(d => new
            {
                severity = d.Severity.ToString().ToLowerInvariant(),
                file = d.File,
                line = d.Line,
                element = d.ElementPath,
                component = d.ComponentType,
                property = d.Property,
                selector = d.Selector,
                message = d.Message,
            }),
        };

        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
    }

    private static string? GetOpt(string[] args, params string[] names)
        => ArgParser.GetOption(args, names);

    private static void ShowHelp()
    {
        ConOut.WriteLine("""
vcsdk links [path] [options]

Scans WorldML files for reference attributes and resolves them against each
scene's element/component tree. Reference properties are read from
components.schema.xml when present (run `vcsdk schema`), otherwise attributes
ending in "Ref" are checked for existence only.

Options:
  -s, --schema <path>       components.schema.xml to use
  -m, --manifest <path>     Also validate manifest world/asset/script paths
      --templates <dir>     Extra template directory (repeatable). templates/
                            and worlds/*/templates/ are discovered automatically.
      --fix                 Rewrite non-canonical selectors to the shortest unambiguous form
      --dry-run             Report fixes without writing files
      --strict              Exit 1 when any reference is unresolved or ambiguous
      --json                Machine-readable report
  -h, --help                Show help

Selectors:
  Door B                    unique element name
  Doors/Door B              suffix path
  Door B[2]                 second sibling named Door B
  @9f3a...                  explicit uid pin
  Door B#PortalComponent    component of that type on the element
  Door B#frame              component by its name attribute
  Door B#MeshRenderer[2]    second component of that type

Examples:
  vcsdk links ./MyGame
  vcsdk links ./MyGame --strict
  vcsdk links ./MyGame --fix --dry-run
  vcsdk links ./MyGame --schema ./components.schema.xml --strict
""");
    }
}
